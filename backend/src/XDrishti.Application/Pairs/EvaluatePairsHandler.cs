using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Pairs;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.PairResearch;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Pairs;

/// <summary>
/// <c>xd pairs eval</c> (P14-13, ADR 0196): protocol S over the stored trades of the pre-registered primary variant. The ledger is read first: the primary variant must already be a counted trial (<c>extensions.trial_not_in_ledger</c> otherwise, with nothing computed) and N is the family's distinct trials.
/// Appends nothing to the ledger, so a re-run stores the same record id and writes nothing. Only trades of the current settings (notional and charge basis) are judged; the others are counted and printed. The hold-out is read once, after a stored development KEEP, by the explicit flag.
/// </summary>
internal sealed class EvaluatePairsHandler(
    IPairResearchStore store,
    ITestLedger ledger,
    IExtensionEvaluationStore evaluations,
    EvaluationWriter writer,
    IPairSettings settings,
    ICostSheetStore sheets,
    IPairSimulator simulator,
    ILogger<EvaluatePairsHandler> logger) : ICommandHandler<EvaluatePairsCommand, ExtensionEvaluationOutcome>
{
    public const string TrialNotInLedgerCode = "extensions.trial_not_in_ledger";

    public async ValueTask<Result<ExtensionEvaluationOutcome>> HandleAsync(EvaluatePairsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!PairFamilies.IsKnown(command.Family))
        {
            return Result.Failure<ExtensionEvaluationOutcome>(Error.Validation(ExtensionEvaluationErrors.InvalidQuery, "The family is " + string.Join(", ", PairFamilies.All) + "."));
        }

        var options = PairStudyOptions.Default with { PairNotional = settings.PairNotional, MaxCandidates = settings.MaxCandidates };
        if (!options.IsValid)
        {
            return Result.Failure<ExtensionEvaluationOutcome>(Error.Validation(RunPairsHandler.InvalidCode, "The Pairs settings are not in range."));
        }

        // A spent hold-out refuses before anything is read or counted.
        if (command.HoldOut && await evaluations.HoldOutUsedAsync(PairFamilies.Intraday, PairFamilies.VersionKey, cancellationToken))
        {
            PairsLog.Refused(logger, PairFamilies.Intraday, ExtensionEvaluationErrors.HoldOutUsed);
            return Result.Failure<ExtensionEvaluationOutcome>(Error.Conflict(ExtensionEvaluationErrors.HoldOutUsed, "The hold-out of " + PairFamilies.Intraday + " for " + PairFamilies.VersionKey + " has been read already; it is read once."));
        }

        CostSheet? sheet = null;
        if (!string.IsNullOrWhiteSpace(settings.CostSheetId))
        {
            var found = await sheets.FindAsync(settings.CostSheetId, cancellationToken);
            if (found.IsFailure)
            {
                return Result.Failure<ExtensionEvaluationOutcome>(found.Error);
            }

            sheet = found.Value;
        }

        // Ledger first: the primary variant has to be a counted trial before a number is read.
        var primary = PairTrial.Variant(PairVariant.Primary, options, DateOnly.MinValue, DateOnly.MinValue);
        if (!await ledger.HasHashAsync(PairFamilies.Intraday, primary.Hash, cancellationToken))
        {
            PairsLog.Refused(logger, PairFamilies.Intraday, TrialNotInLedgerCode);
            return Result.Failure<ExtensionEvaluationOutcome>(Error.Validation(TrialNotInLedgerCode, "The primary variant " + PairVariant.Primary.Label + " is not in the test ledger of " + PairFamilies.Intraday + ": run 'xd pairs run' first."));
        }

        var trials = await writer.CountAsync(PairFamilies.Intraday, cancellationToken);
        var config = PairConfig.Of(options, sheet);
        var stored = await store.ReadTradesAsync("cfg:" + config, cancellationToken);
        var formationRows = await store.ReadFormationsAsync(PairFamilies.Intraday, cancellationToken);

        // The hold-out is cut first, from the primary variant's dates: everything printed or stored for the development verdict (grid, formations, turnover, counts) is built from the development trades alone.
        var holdStart = ProtocolS.Cut([.. stored.Where(t => IsPrimary(t)).Select(ToSample)], options).HoldOutStart;
        var devStored = stored.Where(t => holdStart is null || t.Date < holdStart).ToList();
        var chosen = Choose(devStored, formationRows);
        var kept = devStored.Where(t => Kept(t, chosen)).ToList();
        var ignored = await store.CountOtherTradesAsync("cfg:" + config, holdStart ?? DateOnly.MaxValue, cancellationToken) + (devStored.Count - kept.Count);
        var grid = PairVariant.Grid.Select(v =>
        {
            var trades = kept.Where(t => Variant(t) == "variant:" + v.Label).ToList();
            return new PairGridLine(v.Label, trades.Count, trades.Count > 0 ? trades.Average(t => t.R) : null);
        }).ToList();
        var samples = kept.Where(t => IsPrimary(t)).Select(ToSample).ToList();
        var formations = formationRows.Where(f => (holdStart is null || QuarterStart(f.Stat.Quarter) < holdStart) && IsFormationOf(f, chosen)).Select(f => f.Stat).ToList();

        var evaluated = ProtocolS.Evaluate(new ProtocolSInput(samples, trials, grid, formations, options, holdStart));
        if (evaluated.IsFailure)
        {
            return Result.Failure<ExtensionEvaluationOutcome>(evaluated.Error);
        }

        var result = evaluated.Value;
        var development = Identity(options, primary.Hash, trials, samples, formations.Count, ignored, sheet, config, false, null).ToRecord(result.Outcome);
        ExtensionEvaluationRecord? holdRecord = null;
        string? note = null;
        if (command.HoldOut)
        {
            if (result.Outcome.Verdict != ExtensionVerdict.Keep)
            {
                note = "The hold-out was not read: the development verdict is not KEEP.";
            }
            else
            {
                var allChosen = Choose([.. stored], formationRows);
                var all = stored.Where(t => Kept(t, allChosen) && IsPrimary(t)).Select(ToSample).ToList();
                var hold = ProtocolS.HoldOut(all, options);
                if (hold.IsFailure)
                {
                    return Result.Failure<ExtensionEvaluationOutcome>(hold.Error);
                }

                holdRecord = Identity(options, primary.Hash, trials, samples, formations.Count, ignored, sheet, config, true, development.EvaluationId).ToRecord(hold.Value);
            }
        }

        var records = new List<EvaluatedRecord>();
        foreach (var record in holdRecord is null ? [development] : new[] { development, holdRecord })
        {
            var written = await writer.StoreAsync(record, cancellationToken);
            if (written.IsFailure)
            {
                return Result.Failure<ExtensionEvaluationOutcome>(written.Error);
            }

            PairsLog.Evaluated(logger, record.Family, record.VersionKey, record.Verdict, record.Rows, written.Value.Stored);
            records.Add(written.Value);
        }

        if (ignored > 0)
        {
            note = (note is null ? string.Empty : note + "\n") + string.Create(CultureInfo.InvariantCulture, $"{ignored} stored trades of other settings (options, notional or charge basis) or of superseded formations were not judged.");
        }

        note = (note is null ? string.Empty : note + "\n") + string.Join('\n', Summary(grid, formations, samples, trials));

        return Result.Success(new ExtensionEvaluationOutcome(PairFamilies.Intraday, records, note));
    }

    private static IEnumerable<string> Summary(List<PairGridLine> grid, List<PairFormationStat> formations, List<PairTradeSample> samples, int trials)
    {
        var c = CultureInfo.InvariantCulture;
        yield return string.Create(c, $"ledger: N {trials}, F {MultipleTesting.Factor(trials):F2}; variant grid: {string.Join(", ", grid.Select(g => g.Variant + " n=" + g.Trades.ToString(c) + " meanR=" + (g.MeanR is { } m ? m.ToString("F3", c) : "unavailable")))}");
        var halfLives = formations.SelectMany(f => f.HalfLives).Order().ToArray();
        yield return string.Create(c, $"formations {formations.Count}: selected per formation [{string.Join(", ", formations.Select(f => f.Quarter + "=" + f.Selected.ToString(c)))}]; without a selected pair {(formations.Count > 0 ? formations.Count(f => f.Selected == 0) / (double)formations.Count * 100 : 0):F0} %");
        yield return halfLives.Length == 0
            ? "half-life (sessions): unavailable"
            : string.Create(c, $"half-life (sessions): n {halfLives.Length}, min {halfLives[0]:F1}, median {halfLives[halfLives.Length / 2]:F1}, max {halfLives[^1]:F1}");
        yield return samples.Count == 0 ? "turnover per trade: unavailable" : string.Create(c, $"turnover per trade: {samples.Sum(t => t.TurnoverInr) / samples.Count:F0} INR over {samples.Count} primary-variant trades");
    }

    private static string Variant(PairStoredTrade t) => t.Labels.FirstOrDefault(l => l.StartsWith("variant:", StringComparison.Ordinal)) ?? "variant:unknown";

    private static bool IsPrimary(PairStoredTrade t) => Variant(t) == "variant:" + PairVariant.Primary.Label;

    private static PairTradeSample ToSample(PairStoredTrade t) => new(t.Date, t.EntryEpochSeconds, t.PairKey, t.R, t.GrossInr, t.NetInr, Turnover(t));

    private static string? Label(PairStoredTrade t, string prefix) => t.Labels.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));

    private static DateOnly QuarterStart(string label) =>
        label.Length == 6 && int.TryParse(label[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var year) && int.TryParse(label[5..], NumberStyles.None, CultureInfo.InvariantCulture, out var q) && q is >= 1 and <= 4
            ? new DateOnly(year, ((q - 1) * 3) + 1, 1)
            : DateOnly.MaxValue;

    /// <summary>For each quarter the one formation judged: the latest stored one (by time, then id) among those the quarter's trades carry; a quarter's trades of an earlier formation are superseded.</summary>
    private static Dictionary<string, string> Choose(List<PairStoredTrade> trades, IReadOnlyList<StoredFormation> formations)
    {
        var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var quarter in trades.Select(t => Label(t, "quarter:")).Where(q => q is not null).Distinct())
        {
            var carried = trades.Where(t => Label(t, "quarter:") == quarter).Select(t => Label(t, "formation:")).Where(f => f is not null).Distinct().ToList();
            var latest = formations.Where(f => carried.Contains("formation:" + f.FormationId[..16], StringComparer.Ordinal)).OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.FormationId, StringComparer.Ordinal).FirstOrDefault();
            chosen[quarter!] = latest is null ? carried.OrderBy(f => f, StringComparer.Ordinal).First()! : "formation:" + latest.FormationId[..16];
        }

        return chosen;
    }

    private static bool Kept(PairStoredTrade t, Dictionary<string, string> chosen) =>
        Label(t, "quarter:") is not { } quarter || Label(t, "formation:") is not { } formation || !chosen.TryGetValue(quarter, out var wanted) || wanted == formation;

    /// <summary>A formation counts when it is the judged one of its quarter, or its quarter has no trades to say which.</summary>
    private static bool IsFormationOf(StoredFormation f, Dictionary<string, string> chosen) =>
        !chosen.TryGetValue("quarter:" + f.Stat.Quarter, out var wanted) || wanted == "formation:" + f.FormationId[..16];

    private static decimal Turnover(PairStoredTrade trade)
    {
        var label = trade.Labels.FirstOrDefault(l => l.StartsWith("turnover_inr:", StringComparison.Ordinal));
        return label is not null && decimal.TryParse(label["turnover_inr:".Length..], NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;
    }

    private ProtocolIdentity Identity(PairStudyOptions options, string trialHash, int trials, List<PairTradeSample> samples, int formations, int ignored, CostSheet? sheet, string config, bool holdOut, string? developmentRecord)
    {
        var tradesHash = ExtensionFamilies.Sha256Hex(string.Join('\n', samples.Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.EntryEpochSeconds}|{s.PairKey}|{s.R:R}")).Order(StringComparer.Ordinal)));
        return new ProtocolIdentity(
            PairFamilies.Intraday,
            PairFamilies.Protocol,
            null,
            PairFamilies.VersionKey,
            "none",
            ExtensionFamilies.Sha256Hex(options.Canonical()),
            null,
            trialHash,
            trials,
            "quarter",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["charge_basis"] = sheet is null ? "gross" : "sheet:" + sheet.Id + ":" + sheet.RatesSha256,
                ["config"] = config,
                ["development_record"] = developmentRecord ?? "none",
                ["fill_engine"] = simulator.FillEngineVersion,
                ["formations"] = formations.ToString(CultureInfo.InvariantCulture),
                ["ignored_trades"] = ignored.ToString(CultureInfo.InvariantCulture),
                ["pair_walker"] = simulator.WalkerVersion,
                ["primary_variant"] = PairVariant.Primary.Label,
                ["trades"] = samples.Count.ToString(CultureInfo.InvariantCulture),
                ["trades_sha256"] = tradesHash,
            },
            holdOut);
    }
}
