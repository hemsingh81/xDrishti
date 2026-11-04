using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Evaluation;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Application.OptionExpression;

/// <summary>
/// <c>xd options expression-eval</c> (P14-09, ADR 0198): protocol O over an expression run (P14-08) of a stored full-test cash run. <b>Order, enforced here and pinned by a test:</b> a spent hold-out is refused, then the run and the profile are looked up, then the trial (profile on this population) is appended to the test ledger and its hash is read back,
/// and only then is any date, count or figure read. The development windows are read with the hold-out cut off in the query; the hold-out is read once, after a development KEEP, by the explicit flag. A re-run appends nothing to the ledger and stores nothing new (the same evaluation id).
/// Research only: nothing selects, plans or orders; the expression stays cash.
/// </summary>
internal sealed class EvaluateExpressionHandler(
    IRunStore runs,
    IExpressionProfileStore profiles,
    IExpressionReader reader,
    IOptionReference reference,
    IRegimeLabels regime,
    ISpreadModelStore spreadModels,
    ISpreadModelSettings spreadSettings,
    ITestLedger ledger,
    IExtensionEvaluationStore evaluations,
    EvaluationWriter writer,
    ILogger<EvaluateExpressionHandler> logger) : ICommandHandler<EvaluateExpressionCommand, ExtensionEvaluationOutcome>
{
    public const string TrialNotInLedgerCode = "extensions.trial_not_in_ledger";
    public const string RunNotFullCode = "expression.run_not_full";
    public const string HoldOutPrimaryOnlyCode = "expression.holdout_primary_only";
    public const string RemarkNotFoundCode = "expression.remark_not_found";
    public const string ProfileKindMismatchCode = "expression.profile_kind_mismatch";
    private const string FullTestOrigin = "lab:full";
    private static readonly string[] IndexSymbols = [ExpiryRules.Nifty, ExpiryRules.BankNifty];

    public async ValueTask<Result<ExtensionEvaluationOutcome>> HandleAsync(EvaluateExpressionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!ExpressionFamilies.IsKnown(command.Family))
        {
            return Result.Failure<ExtensionEvaluationOutcome>(Error.Validation(ExtensionEvaluationErrors.InvalidQuery, "The family is " + string.Join(", ", ExpressionFamilies.All) + "."));
        }

        var condor = command.Family == ExpressionFamilies.IronCondor;
        var protocol = ExpressionFamilies.ProtocolOf(command.Family);
        var options = condor ? ExpressionStudyOptions.Condor : ExpressionStudyOptions.Default;

        // A spent hold-out refuses before anything is looked up, counted or read.
        if (command.HoldOut && await evaluations.HoldOutUsedAsync(command.Family, ExpressionFamilies.VersionKey, cancellationToken))
        {
            ExpressionEvalLog.Refused(logger, command.Family, ExtensionEvaluationErrors.HoldOutUsed);
            return Result.Failure<ExtensionEvaluationOutcome>(Error.Conflict(ExtensionEvaluationErrors.HoldOutUsed, "The hold-out of " + command.Family + " for " + ExpressionFamilies.VersionKey + " has been read already; it is read once."));
        }

        var primary = ExpressionProfiles.PrimaryHashOf(command.Family)!;
        var profileHash = command.ProfileHash ?? primary;
        if (command.HoldOut && profileHash != primary)
        {
            return Refused(command.Family, HoldOutPrimaryOnlyCode, Error.Validation(HoldOutPrimaryOnlyCode, "The hold-out is read once for the whole family: only the pre-registered primary profile may read it."));
        }

        var storedProfile = await profiles.GetAsync(profileHash, cancellationToken);
        if (storedProfile is null)
        {
            return Refused(command.Family, RemarkRunHandler.ProfileNotFoundCode, Error.NotFound(RemarkRunHandler.ProfileNotFoundCode, "No expression profile has that hash."));
        }

        if (storedProfile.Profile.Kind != ExpressionProfiles.KindOf(command.Family))
        {
            return Refused(command.Family, ProfileKindMismatchCode, Error.Validation(ProfileKindMismatchCode, "That profile is not of the kind the family judges: each structure is its own family."));
        }

        // The condor has no cash run: its population is the one the condor verb printed. Every other family is a stored full-test cash run.
        StoredRunHeader? header = null;
        if (!condor)
        {
            header = await runs.FindAsync(command.RunKey, cancellationToken);
            if (header is null)
            {
                return Refused(command.Family, RemarkRunHandler.RunNotFoundCode, Error.NotFound(RemarkRunHandler.RunNotFoundCode, "No stored run has that key."));
            }

            if (!string.Equals(header.CreatedBy, FullTestOrigin, StringComparison.Ordinal))
            {
                return Refused(command.Family, RunNotFullCode, Error.Validation(RunNotFullCode, "Only the runs of a full Lab test are judged: their out-of-sample windows are the population (re-marking in-sample trades would flatter both sides)."));
            }
        }

        // The credit gates: a stored regime and a measured half-spread. A failed gate is a NOT_EVALUABLE record that counts no trial and reads no bar, no date and no figure.
        if (command.Family is ExpressionFamilies.CreditSpread or ExpressionFamilies.IronCondor)
        {
            var missing = StructureGate.Missing(await regime.AnyAsync(cancellationToken), await BestMeasuredSessionsAsync(cancellationToken), spreadSettings.MinUsableSessions);
            if (missing.Count > 0)
            {
                return await GateAsync(command, options, protocol, profileHash, primary, missing, cancellationToken);
            }
        }

        var remarked = await reader.FindRunAsync(command.RunKey, profileHash, cancellationToken);
        if (remarked is null)
        {
            return Refused(command.Family, RemarkNotFoundCode, Error.NotFound(RemarkNotFoundCode, condor ? "That population has not been simulated under that profile: run 'xd options condors' first." : "That run has not been re-marked under that profile: run 'xd options remark' first."));
        }

        var from = header?.From ?? remarked.From;
        var to = header?.To ?? remarked.To;
        if (from is not { } fromDate || to is not { } toDate)
        {
            return Refused(command.Family, RemarkNotFoundCode, Error.NotFound(RemarkNotFoundCode, "The condor population carries no dates."));
        }

        // Ledger first: the trial is a counted look before a date, a count or a figure of the run is read.
        var trial = ExpressionTrial.Of(command.Family, profileHash, command.RunKey, fromDate, toDate);
        var trials = await writer.AppendTrialAsync(trial, command.RunKey, fromDate, toDate, "expression-trial", cancellationToken);
        if (!await ledger.HasHashAsync(command.Family, trial.Hash, cancellationToken))
        {
            return Refused(command.Family, TrialNotInLedgerCode, Error.Validation(TrialNotInLedgerCode, "The trial is not in the test ledger of " + command.Family + ": nothing was computed."));
        }

        var epochFrom = ExpressionEpochs.LatestFrom(await reference.EpochsAsync(cancellationToken));
        var dates = await reader.DatesAsync(remarked.RunId, cancellationToken);
        var windows = condor ? [.. dates.Sides.Select(side => new ExpressionWindow(side.Side, side.First, side.Last))] : ExpressionWindows.Of(dates.Sides);

        // The hold-out is cut first, from dates only: everything printed or stored for the development verdict is built from the development windows.
        DateOnly? holdStart = null;
        if (dates.Sides.Count > 0 && windows.Count > 0)
        {
            // the cut depends on the closed trades' dates and the windows only, never on which trades were paired
            holdStart = ProtocolO.HoldOutStartOf(DateOnly.FromDayNumber(Math.Min(dates.Sides.Max(s => s.Last).DayNumber, windows.Max(w => w.End).DayNumber)), options);
        }

        var devWindows = ExpressionWindows.Before(windows, holdStart);
        var rows = await reader.PairedAsync(remarked.RunId, devWindows, cancellationToken);
        var unpaired = await reader.UnpairedAsync(remarked.RunId, devWindows, cancellationToken);
        var pairs = rows.Select(ToPair).ToList();

        var input = new ProtocolOInput(pairs, unpaired, trials, epochFrom, holdStart, options, dates.LastPaired);
        var evaluated = condor ? ProtocolR.Evaluate(input) : ProtocolO.Evaluate(input);
        if (evaluated.IsFailure)
        {
            return Result.Failure<ExtensionEvaluationOutcome>(evaluated.Error);
        }

        var result = evaluated.Value;
        var (development, _) = ProtocolO.Cut(pairs, options, holdStart);
        var record = Identity(options, protocol, trial.Hash, trials, command, profileHash, primary, remarked.RunId, development, devWindows, unpaired, holdStart, epochFrom, false, null).ToRecord(result.Outcome);
        ExtensionEvaluationRecord? holdRecord = null;
        string? note = null;
        if (command.HoldOut)
        {
            if (result.Outcome.Verdict != ExtensionVerdict.Keep || holdStart is not { } start)
            {
                note = "The hold-out was not read: the development verdict is not KEEP.";
            }
            else
            {
                var holdRows = await reader.PairedAsync(remarked.RunId, ExpressionWindows.From(windows, start), cancellationToken);
                var holdPairs = holdRows.Select(ToPair).ToList();
                var hold = condor ? ProtocolR.HoldOut(holdPairs, options, start) : ProtocolO.HoldOut(holdPairs, options, start);
                if (hold.IsFailure)
                {
                    return Result.Failure<ExtensionEvaluationOutcome>(hold.Error);
                }

                holdRecord = Identity(options, protocol, trial.Hash, trials, command, profileHash, primary, remarked.RunId, development, devWindows, unpaired, holdStart, epochFrom, true, record.EvaluationId).ToRecord(hold.Value);
            }
        }

        var stored = new List<EvaluatedRecord>();
        foreach (var item in holdRecord is null ? [record] : new[] { record, holdRecord })
        {
            var written = await writer.StoreAsync(item, cancellationToken);
            if (written.IsFailure)
            {
                return Result.Failure<ExtensionEvaluationOutcome>(written.Error);
            }

            ExpressionEvalLog.Evaluated(logger, item.Family, item.VersionKey, item.Verdict, item.Rows, written.Value.Stored);
            stored.Add(written.Value);
        }

        var lines = new List<string>(result.Lines)
        {
            string.Create(CultureInfo.InvariantCulture, $"out-of-sample windows {devWindows.Count} (hold-out starts {(holdStart is { } h ? h.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "unavailable")}); profile {profileHash[..12]}{(profileHash == primary ? " (primary)" : " (secondary)")}"),
        };
        if (note is not null)
        {
            lines.Insert(0, note);
        }

        return Result.Success(new ExtensionEvaluationOutcome(command.Family, stored, string.Join('\n', lines)));
    }

    /// <summary>The largest session count of a stored measured model of an index underlying (the credit structures are for indices); null when no model is stored. An owner assumption is not a model.</summary>
    private async ValueTask<int?> BestMeasuredSessionsAsync(CancellationToken cancellationToken)
    {
        int? best = null;
        foreach (var symbol in IndexSymbols)
        {
            if (await reference.FindUnderlyingAsync(symbol, cancellationToken) is { } id && await spreadModels.LatestAsync(id, cancellationToken) is { } model)
            {
                best = Math.Max(best ?? 0, model.Sessions);
            }
        }

        return best;
    }

    /// <summary>Stores the NOT_EVALUABLE record of a failed credit gate: no trial counted, nothing read.</summary>
    private async ValueTask<Result<ExtensionEvaluationOutcome>> GateAsync(EvaluateExpressionCommand command, ExpressionStudyOptions options, string protocol, string profileHash, string primary, IReadOnlyList<string> missing, CancellationToken cancellationToken)
    {
        var trials = await writer.CountAsync(command.Family, cancellationToken);
        var outcome = StructureGate.NotEvaluable(protocol, missing);
        var record = Identity(options, protocol, null, trials, command, profileHash, primary, "none", [], [], new Dictionary<string, int>(), null, ExpressionEpochs.DefaultLatestFrom, false, null).ToRecord(outcome);
        var written = await writer.StoreAsync(record, cancellationToken);
        if (written.IsFailure)
        {
            return Result.Failure<ExtensionEvaluationOutcome>(written.Error);
        }

        ExpressionEvalLog.Evaluated(logger, record.Family, record.VersionKey, record.Verdict, 0, written.Value.Stored);
        return Result.Success(new ExtensionEvaluationOutcome(command.Family, [written.Value], string.Join('\n', missing.Select(m => "missing: " + m))));
    }

    private Result<ExtensionEvaluationOutcome> Refused(string family, string code, Error error)
    {
        ExpressionEvalLog.Refused(logger, family, code);
        return Result.Failure<ExtensionEvaluationOutcome>(error);
    }

    private static ExpressionPair ToPair(ExpressionPairRow r) => ExpressionPair.Of(r.TradeKey, r.EntryTs, r.ExitTs, r.OptionR, r.CashR, r.Net, r.ExpirySource, r.Sizable, r.Labels);

    private static ProtocolIdentity Identity(
        ExpressionStudyOptions options, string protocol, string? trialHash, int trials, EvaluateExpressionCommand command, string profileHash, string primary, string expressionRun, IReadOnlyList<ExpressionPair> development, IReadOnlyList<ExpressionWindow> windows,
        IReadOnlyDictionary<string, int> unpaired, DateOnly? holdStart, DateOnly epochFrom, bool holdOut, string? developmentRecord)
    {
        var c = CultureInfo.InvariantCulture;
        var tradesHash = ExtensionFamilies.Sha256Hex(string.Join('\n', development.Select(p => string.Create(c, $"{p.EntryEpochSeconds}|{p.TradeKey}|{p.OptionR:R}|{p.CashR:R}|{p.Net}")).Order(StringComparer.Ordinal)));
        var windowsHash = ExtensionFamilies.Sha256Hex(string.Join('\n', windows.Select(w => string.Create(c, $"{w.Side}|{w.Start:yyyy-MM-dd}|{w.End:yyyy-MM-dd}"))));
        var basis = development.Count == 0 ? "none" : development.All(p => p.Net) ? "net" : development.Any(p => p.Net) ? "mixed" : "gross";
        return new ProtocolIdentity(
            command.Family,
            protocol,
            null,
            ExpressionFamilies.VersionKey,
            "none",
            ExtensionFamilies.Sha256Hex(options.Canonical()),
            null,
            trialHash,
            trials,
            "quarter",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                [command.Family == ExpressionFamilies.IronCondor ? "condor_population" : "cash_run"] = command.RunKey,
                ["charge_basis"] = basis,
                ["development_record"] = developmentRecord ?? "none",
                ["expression_run"] = expressionRun,
                ["hold_out_start"] = holdStart?.ToString("yyyy-MM-dd", c) ?? "none",
                ["latest_epoch_from"] = epochFrom.ToString("yyyy-MM-dd", c),
                ["oos_windows_sha256"] = windowsHash,
                ["paired"] = development.Count.ToString(c),
                ["primary_profile"] = profileHash == primary ? "true" : "false",
                ["profile"] = profileHash,
                ["trades_sha256"] = tradesHash,
                ["unpaired"] = string.Join(',', unpaired.OrderBy(u => u.Key, StringComparer.Ordinal).Select(u => u.Key + "=" + u.Value.ToString(c))),
            },
            holdOut);
    }
}
