using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Learning;

/// <summary>
/// Protocol X for <c>ev-1</c> or <c>fu-1</c> (P15-10): the extraction records of the stored runs gate the verdict first (an unverified parser or stale rows are NOT_EVALUABLE with the reason named, before any number is computed and without counting a trial); then the development rows (all but the
/// reserved last four seasons, purged) are compared by the P13-05 core with quarterly folds and season resampling, after the trial's ledger row is appended. The hold-out is read only by the explicit act, after a stored KEEP, once.
/// </summary>
internal sealed class ComparisonEvaluation(
    IExtensionTrainingSource source,
    IModelTrainer trainer,
    IFilingSettings settings,
    ITradingCalendar calendars,
    EvaluationWriter writer,
    Abstractions.Lab.ITestLedger ledger,
    IExtensionEvaluationStore store,
    ILogger<ComparisonEvaluation> logger,
    string? currentParserVersion = null)
{
    private const string Method = "extension-compare";

    public async ValueTask<Result<ExtensionEvaluationOutcome>> RunAsync(string family, bool holdOut, CancellationToken cancellationToken)
    {
        var version = ExtensionFamilies.ExtensionVersionOf(family);
        var group = ExtensionCatalogs.GroupOf(version);
        if (version is null || group is null)
        {
            return Result.Failure<ExtensionEvaluationOutcome>(ExtensionEvaluationErrors.InvalidFamily(family));
        }

        // A spent hold-out refuses before the trial is counted or anything is written: a refused --holdout must not raise N.
        // shortcut: check-then-store is not atomic across two concurrent runs of the verb; the CLI has one owner and the table's unique index is the backstop. Add a per-family advisory lock if a second caller ever exists.
        if (holdOut && await store.HoldOutUsedAsync(family, ExtensionReport.VersionKeyOf(group), cancellationToken))
        {
            ExtensionEvaluationLog.Refused(logger, family, ExtensionEvaluationErrors.HoldOutUsed);
            return Result.Failure<ExtensionEvaluationOutcome>(Error.Conflict(ExtensionEvaluationErrors.HoldOutUsed, "The hold-out of " + family + " for " + ExtensionReport.VersionKeyOf(group) + " has been read already; it is read once."));
        }

        var runs = (await source.ListRunsAsync(version, cancellationToken)).Where(r => r.ClosedTrades > 0).ToArray();
        var extracted = runs.Where(r => r.Record is not null).ToArray();
        var sources = Sources(version, runs, extracted);
        var recipeHash = ExtensionFamilies.Sha256Hex(trainer.Candidate.Name + ":" + ExtensionFamilies.Sha256Hex(trainer.Parameters));
        ProtocolIdentity Identity(int trials, string? trialHash, string? sourceId) =>
            new(family, ProtocolX.Protocol, version, ExtensionReport.VersionKeyOf(group), TradeFeatureCatalog.Version, recipeHash, sourceId, trialHash, trials, "quarter", sources, false);

        // Gate first: nothing is read or computed when the records say the rows cannot be judged.
        if (extracted.Length == 0)
        {
            return await NotEvaluableAsync(family, Identity(await writer.CountAsync(family, cancellationToken), null, null), ProtocolX.NotEvaluable([string.Create(CultureInfo.InvariantCulture, $"no_extraction: extraction records 0 of {runs.Length} runs (xd learn features --extension {version})")], 0), cancellationToken);
        }

        // Every run with closed trades goes to the gate: a run whose extraction was never made is stale rows, not a run to leave out quietly.
        var problems = ExtensionGate.Problems(version, [.. runs.Select(r => (r.RunKey, r.ClosedTrades, r.Record))], settings.LagMinutes, currentParserVersion ?? ResultsParser.Version);   // the parser version in force: the constant, unless a test names another
        if (problems.Count > 0)
        {
            return await NotEvaluableAsync(family, Identity(await writer.CountAsync(family, cancellationToken), null, null), ProtocolX.NotEvaluable(problems, runs.Sum(r => r.ClosedTrades)), cancellationToken);
        }

        var loaded = await source.LoadAsync(group, [.. runs.Select(r => r.RunKey)], cancellationToken);
        if (loaded.IsFailure)
        {
            return await NotEvaluableAsync(family, Identity(await writer.CountAsync(family, cancellationToken), null, null), ProtocolX.NotEvaluable(["training data: " + loaded.Error.Code], runs.Sum(r => r.ClosedTrades)), cancellationToken);
        }

        var data = loaded.Value;
        var seasons = SeasonFolds.Distinct(data.Rows.Select(r => r.TradeDate));
        var start = SeasonFolds.HoldOutStart(seasons);
        if (start is not { } holdOutStart)
        {
            return await NotEvaluableAsync(family, Identity(await writer.CountAsync(family, cancellationToken), null, data.TrainingDataId), ProtocolX.NotEvaluable([string.Create(CultureInfo.InvariantCulture, $"seasons {seasons.Count} of {SeasonFolds.HoldOutSeasons + 1} (the last {SeasonFolds.HoldOutSeasons} are reserved as the hold-out)")], data.Rows.Count), cancellationToken);
        }

        var reserved = data.Rows.Where(r => r.TradeDate >= holdOutStart).ToArray();
        var firstDecision = reserved.Min(r => r.DecisionEpochSeconds);
        var development = data.Rows.Where(r => r.TradeDate < holdOutStart && r.ResolvedEpochSeconds + HoldOut.BarSeconds < firstDecision).ToArray();
        if (development.Length == 0)
        {
            return await NotEvaluableAsync(family, Identity(await writer.CountAsync(family, cancellationToken), null, data.TrainingDataId), ProtocolX.NotEvaluable(["development rows 0 (after the hold-out purge)"], 0), cancellationToken);
        }

        var options = ProtocolX.Options(ExtensionFamilies.Sha256Hex(trainer.Parameters), data.LabelRuleVersion, holdOutStart);
        var from = development.Min(r => r.TradeDate);
        var to = development.Max(r => r.TradeDate);
        var calendar = await calendars.GetAsync(cancellationToken);
        var sessions = new List<DateOnly>();
        for (var d = from.AddDays(-40); d <= to.AddDays(40); d = d.AddDays(1))
        {
            if (calendar.IsTradingDay(d))
            {
                sessions.Add(d);
            }
        }

        // Ledger first: the trial is counted before the core is called; the core refuses a trial it does not find there.
        var trial = ExtensionTrial.Of(group, trainer.Candidate.Name, options, from, to);
        await writer.AppendTrialAsync(trial, data.TrainingDataId, from, to, Method, cancellationToken);
        var hashes = (await ledger.ListAsync(family, cancellationToken)).Select(r => r.StrategyHash).ToArray();
        var compared = ProtocolX.Compare(development, data.BaseWidth, group, trainer.Candidate, hashes, sessions, options);
        if (compared.IsFailure)
        {
            return await NotEvaluableAsync(family, Identity(Math.Max(1, hashes.Distinct(StringComparer.Ordinal).Count()), trial.Hash, data.TrainingDataId), ProtocolX.NotEvaluable(["comparison refused: " + compared.Error.Code], development.Length), cancellationToken);
        }

        var report = compared.Value;
        var record = ProtocolX.WithSources(ProtocolX.WithStyleCounts(report.ToRecord(data.TrainingDataId), StyleCounts(development, report)), sources);

        // Everything is computed before anything is written: a refused second reading leaves no record.
        ExtensionEvaluationRecord? holdRecord = null;
        string? note = null;
        if (holdOut)
        {
            if (report.Verdict != ExtensionVerdict.Keep || report.DeltaR is not { } deltaR)
            {
                note = "The hold-out was not read: the development verdict is not KEEP.";
                ExtensionEvaluationLog.HoldOutNotRead(logger, family, "no_development_keep");
            }
            else
            {
                var checkedOut = ExtensionHoldOutCheck.Check(development, reserved, data.BaseWidth, group, trainer.Candidate, deltaR, sessions, options);
                if (checkedOut.IsFailure)
                {
                    return Result.Failure<ExtensionEvaluationOutcome>(checkedOut.Error);
                }

                holdRecord = ProtocolX.WithSources(ExtensionHoldOutRecords.Build(report, checkedOut.Value, data.TrainingDataId), new SortedDictionary<string, string>(sources, StringComparer.Ordinal) { ["development_record"] = record.EvaluationId });
            }
        }

        var records = new List<EvaluatedRecord>();
        foreach (var toStore in holdRecord is null ? [record] : new[] { record, holdRecord })
        {
            var stored = await writer.StoreAsync(toStore, cancellationToken);
            if (stored.IsFailure)
            {
                return Result.Failure<ExtensionEvaluationOutcome>(stored.Error);
            }

            ExtensionEvaluationLog.Stored(logger, toStore.Family, toStore.Protocol, toStore.VersionKey, toStore.Verdict, toStore.Rows, stored.Value.Stored);
            records.Add(stored.Value);
        }

        return Result.Success(new ExtensionEvaluationOutcome(family, records, note));
    }

    // Rows and paired candidates (development rows inside an evaluable fold) per trade style; the style is the fourth part of the cell key. Counts only: the comparison is pooled and no per-style verdict is made.
    private static Dictionary<string, (int Rows, int Candidates)> StyleCounts(IReadOnlyList<ValidationRow> rows, ExtensionReport report)
    {
        var counts = new Dictionary<string, (int Rows, int Candidates)>(StringComparer.Ordinal) { ["intraday"] = (0, 0), ["swing"] = (0, 0) };
        foreach (var row in rows)
        {
            var parts = row.CellKey.Split('|');
            var style = parts.Length > 3 && parts[3] == "swing" ? "swing" : parts.Length > 3 && parts[3] == "intraday" ? "intraday" : "other";
            var inFold = report.Folds.Any(f => f.Evaluable && row.TradeDate >= f.Start && row.TradeDate <= f.End);
            var current = counts.GetValueOrDefault(style);
            counts[style] = (current.Rows + 1, current.Candidates + (inFold ? 1 : 0));
        }

        return counts;
    }

    // shortcut: the calendar hash in the record comes from the extraction records (what the rows were built from), not from a fresh snapshot read, so a calendar that changed under unchanged versions is not detected here. A fresh read per instrument set would cost one snapshot per evaluation; add it if the calendar is ever edited in place.
    private static SortedDictionary<string, string> Sources(string version, ExtensionRunSource[] runs, ExtensionRunSource[] extracted)
    {
        var records = extracted.Select(r => r.Record!).ToArray();
        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["extension_version"] = version,
            ["lag_minutes"] = string.Join(',', records.Select(r => r.LagMinutes).Distinct().Order().Select(m => m.ToString(CultureInfo.InvariantCulture))),
            ["parser_version"] = string.Join(',', records.Select(r => r.ParserVersion ?? "none").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
            ["calendar_sha256"] = string.Join(',', records.Select(r => r.CalendarSha256).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
            ["filings_watermark"] = records.Select(r => r.FilingsWatermark).Where(w => w is not null).Max()?.ToString("O", CultureInfo.InvariantCulture) ?? "none",
            ["run_keys_sha256"] = ExtensionFamilies.Sha256Hex(string.Join(',', extracted.Select(r => r.RunKey).Order(StringComparer.Ordinal))),
            ["runs_extracted"] = extracted.Length.ToString(CultureInfo.InvariantCulture) + " of " + runs.Length.ToString(CultureInfo.InvariantCulture),
        };
    }

    private async ValueTask<Result<ExtensionEvaluationOutcome>> NotEvaluableAsync(string family, ProtocolIdentity identity, ProtocolOutcome outcome, CancellationToken cancellationToken)
    {
        var record = identity.ToRecord(outcome);
        var stored = await writer.StoreAsync(record, cancellationToken);
        if (stored.IsFailure)
        {
            return Result.Failure<ExtensionEvaluationOutcome>(stored.Error);
        }

        ExtensionEvaluationLog.Stored(logger, record.Family, record.Protocol, record.VersionKey, record.Verdict, record.Rows, stored.Value.Stored);
        return Result.Success(new ExtensionEvaluationOutcome(family, [stored.Value], null));
    }
}
