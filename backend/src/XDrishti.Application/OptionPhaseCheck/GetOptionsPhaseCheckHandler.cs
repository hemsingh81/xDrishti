using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionPhaseCheck;
using XDrishti.Application.Abstractions.OptionReports;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.OptionReports;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Evaluation;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Application.OptionPhaseCheck;

/// <summary>
/// The P14 phase check (spec P14-17, ADR 0202): reads the stored verdicts of the seven families, the versions each rests on, the plan items and paper entries with a non-cash cell key, the archive summary, the years of bars and the two archive switches through <b>read calls only</b> and
/// hands plain facts to the pure <see cref="OptionsPhaseCheck"/>. It writes nothing, runs no evaluation and places no order. A source that cannot be read (a missing table included) makes its family open or its figure <c>unavailable</c>; it is never an error of the call and never a MET.
/// </summary>
internal sealed class GetOptionsPhaseCheckHandler(
    IExtensionEvaluationReader evaluations,
    IOptionsPhaseReader phase,
    IOptionReportReader runs,
    IQueryHandler<GetOptionArchiveSummaryQuery, Result<OptionArchiveSummaryView>> archive,
    IBarQueries bars,
    ISimulationEngineVersion engine,
    ICostSheetHashes sheets,
    ICashRunHeaders cashRuns,
    ISpreadModelIds models,
    IRemarkSettings remark,
    IOptionsPhaseCheckSettings settings,
    TimeProvider clock,
    ILogger<GetOptionsPhaseCheckHandler> logger) : IQueryHandler<OptionsPhaseCheckQuery, Result<OptionsPhaseCheckView>>
{
    public const string UnknownFamilyCode = "options.unknown_family";

    private const int RecordsRead = 100;
    private const int RecordsJudged = 10;

    public async ValueTask<Result<OptionsPhaseCheckView>> HandleAsync(OptionsPhaseCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var unknown = query.Cut.Where(c => !OptionsPhaseCheck.Families.Contains(c, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            return Result.Failure<OptionsPhaseCheckView>(Error.Validation(UnknownFamilyCode, "Unknown family in --cut: " + string.Join(", ", unknown.Select(Printable)) + ". Known: " + string.Join(", ", OptionsPhaseCheck.Families) + "."));
        }

        var now = clock.GetUtcNow();
        var families = new List<OptionsFamilyFacts>(OptionsPhaseCheck.Families.Count);
        foreach (var family in OptionsPhaseCheck.Families)
        {
            // A cut family is listed and not read.
            families.Add(query.Cut.Contains(family, StringComparer.Ordinal) ? new OptionsFamilyFacts(family, true, []) : await ReadFamilyAsync(family, cancellationToken));
        }

        var facts = new OptionsPhaseFacts(families, await CellsAsync(cancellationToken), await ArchiveAsync(now, cancellationToken), now, settings.StaleAfterDays, await LatestFetchAsync(cancellationToken));
        var report = OptionsPhaseCheck.Evaluate(facts, query.Cut);
        OptionsPhaseCheckLog.Evaluated(logger, report.Verdict, report.Open.Count, report.Violations.Count, report.Cut.Count);
        return Result.Success(new OptionsPhaseCheckView(report, report.ToText(), report.ToJson()));
    }

    private static string Printable(string text) => new([.. text.Take(40).Select(ch => char.IsControl(ch) ? '?' : ch)]);

    private async ValueTask<OptionsFamilyFacts> ReadFamilyAsync(string family, CancellationToken cancellationToken)
    {
        try
        {
            var stored = await evaluations.ListAsync(family, RecordsRead, cancellationToken);
            var chosen = stored.Select(r => (Record: r, Sources: SourcesOf(r.InputsJson))).Where(x => Counts(family, x.Record, x.Sources)).Take(RecordsJudged).ToList();
            var context = await ContextAsync(chosen.Select(x => x.Sources).ToList(), cancellationToken);
            return new OptionsFamilyFacts(family, true, [.. chosen.Select(x => Fact(family, x.Record, x.Sources, context))]);
        }
#pragma warning disable CA1031 // A store that cannot be read (a missing table included) makes the family open; cancellation is rethrown.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            OptionsPhaseCheckLog.Unreadable(logger, family, failure.GetType().Name);
            return new OptionsFamilyFacts(family, false, []);
        }
    }

    // Which stored records can decide the family: the protocol of the family; for the expression families only the pre-registered primary profile (a secondary profile is counted in N and never decides, ADR 0198).
    private static bool Counts(string family, ExtensionEvaluationRecord record, IReadOnlyDictionary<string, string> sources) => family switch
    {
        _ when IsExpression(family) => record.Protocol is "O" or "R" && sources.GetValueOrDefault("primary_profile") == "true",
        PairFamilies.Intraday => record.Protocol == PairFamilies.Protocol,
        _ => record.Protocol == "X",
    };

    private static bool IsExpression(string family) => family.StartsWith("expr:", StringComparison.Ordinal);

    private static Dictionary<string, string> SourcesOf(string inputsJson)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(inputsJson);
            if (document.RootElement.TryGetProperty("sources", out var element) && element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                {
                    sources[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            // Unreadable inputs record no source: every compared key then reads as not recorded, so the record is stale, never current.
        }

        return sources;
    }

    /// <summary>What the records of one family rest on, read once for the family: the expression runs, their cash runs, the watermark instants, the current spread models and the current sheet hashes.</summary>
    private sealed record Context(
        IReadOnlyDictionary<string, PhaseRunInputs> Runs,
        IReadOnlyDictionary<string, PhaseCashRun> Cash,
        IReadOnlyDictionary<long, DateTimeOffset> Ended,
        IReadOnlyDictionary<string, ExpressionRunStored> Stored,
        IReadOnlyDictionary<Guid, string> Models,
        IReadOnlyDictionary<string, string> SheetShas);

    private async ValueTask<Context> ContextAsync(List<Dictionary<string, string>> sources, CancellationToken cancellationToken)
    {
        var runIds = sources.Select(s => s.GetValueOrDefault("expression_run")).Where(id => id is { Length: 64 }).Select(id => id!).Distinct(StringComparer.Ordinal).ToList();
        var cashKeys = sources.Select(s => s.GetValueOrDefault("cash_run")).Where(k => k is { Length: 64 }).Select(k => k!).Distinct(StringComparer.Ordinal).ToList();
        var runRows = runIds.Count == 0 ? [] : await phase.RunInputsAsync(runIds, cancellationToken);
        var cashRows = new List<PhaseCashRun>(cashKeys.Count);
        foreach (var key in cashKeys)
        {
            if (await cashRuns.FindAsync(key, cancellationToken) is { } header)
            {
                cashRows.Add(header);
            }
        }

        var marks = runRows.Select(r => Watermark(r.InputsJson)).Concat(sources.Select(s => SourceWatermark(s.GetValueOrDefault("archive_watermark")))).Where(w => w > 0).Distinct().ToList();
        var ended = marks.Count == 0 ? new Dictionary<long, DateTimeOffset>() : new Dictionary<long, DateTimeOffset>(await phase.FetchEndedAtAsync(marks, cancellationToken));

        var stored = new Dictionary<string, ExpressionRunStored>(StringComparer.Ordinal);
        foreach (var id in runIds)
        {
            if (await runs.ExpressionRunAsync(id, cancellationToken) is { } found)
            {
                stored[id] = found;
            }
        }

        var underlyings = runRows.SelectMany(r => SpreadEntries(r.InputsJson)).Select(e => e.Underlying).Distinct().ToList();
        var modelIds = new Dictionary<Guid, string>();
        foreach (var underlying in underlyings)
        {
            modelIds[underlying] = await models.LatestAsync(underlying, cancellationToken) ?? "none";
        }

        // The sheet ids whose current rates hash is needed: the cash runs' sheets, the condor's sheet, and the pairs' sheets named in their charge basis.
        var sheetIds = cashRows.Select(c => c.CostsSheetId).Append(remark.CondorCostSheetId).Concat(sources.Select(s => PairSheet(s.GetValueOrDefault("charge_basis"))?.Id)).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).Distinct(StringComparer.Ordinal).ToList();
        var shas = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in sheetIds)
        {
            shas[id] = await sheets.RatesHashAsync(id, cancellationToken) ?? "unavailable";
        }

        return new Context(runRows.ToDictionary(r => r.RunId, StringComparer.Ordinal), cashRows.ToDictionary(c => c.RunKey, StringComparer.Ordinal), ended, stored, modelIds, shas);
    }

    private OptionsRecordFact Fact(string family, ExtensionEvaluationRecord record, Dictionary<string, string> sources, Context context)
    {
        var recorded = new Dictionary<string, string>(StringComparer.Ordinal) { ["version_key"] = record.VersionKey };
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        OptionsWatermarkFact? watermark = null;
        double? derived = null;
        double? fallback = null;
        var basis = "unavailable (protocol X may be gross)";

        if (IsExpression(family))
        {
            current["version_key"] = ExpressionFamilies.VersionKey;
            current["remark_version"] = RemarkRunId.Version;
            current["profile"] = ExpressionProfiles.PrimaryHashOf(family) ?? "unavailable";
            recorded["profile"] = sources.GetValueOrDefault("profile") ?? string.Empty;
            basis = sources.GetValueOrDefault("charge_basis") ?? "none";
            var condor = family == ExpressionFamilies.IronCondor;
            // A gate record (a credit structure with no stored regime or no measured spread) read no run: only its version key and profile can be compared, and it says what is missing.
            var gate = sources.GetValueOrDefault("expression_run") == "none";
            var run = context.Runs.GetValueOrDefault(sources.GetValueOrDefault("expression_run") ?? string.Empty);
            var cash = condor ? null : context.Cash.GetValueOrDefault(sources.GetValueOrDefault("cash_run") ?? string.Empty);
            if (!condor && !gate)
            {
                current["engine"] = engine.EngineVersion;
                if (cash is not null)
                {
                    recorded["engine"] = cash.EngineVersion;
                }
            }

            if (gate)
            {
                current.Remove("remark_version");
            }
            else if (run is not null)
            {
                recorded["remark_version"] = run.RemarkVersion;
                var entries = SpreadEntries(run.InputsJson);
                recorded["spread_model"] = string.Join(';', entries.Select(e => e.Underlying.ToString("N", CultureInfo.InvariantCulture) + "=" + e.Model).Order(StringComparer.Ordinal));
                current["spread_model"] = string.Join(';', entries.Select(e => e.Underlying.ToString("N", CultureInfo.InvariantCulture) + "=" + context.Models.GetValueOrDefault(e.Underlying, "none")).Order(StringComparer.Ordinal));
                var sheet = CostSheetOf(run.InputsJson);
                recorded["cost_sheet"] = sheet;
                var sheetId = condor ? remark.CondorCostSheetId : cash?.CostsSheetId;
                current["cost_sheet"] = sheet == "gross" || string.IsNullOrWhiteSpace(sheetId) ? "gross" : context.SheetShas.GetValueOrDefault(sheetId, "unavailable");
                if (Watermark(run.InputsJson) is var mark and > 0)
                {
                    watermark = new OptionsWatermarkFact(mark, context.Ended.TryGetValue(mark, out var at) ? at : null);
                }
                else
                {
                    watermark = new OptionsWatermarkFact(0, null);
                }
            }

            if (context.Stored.GetValueOrDefault(sources.GetValueOrDefault("expression_run") ?? string.Empty) is { } shown)
            {
                derived = shown.Paired > 0 ? (double)shown.DerivedExpiry / shown.Paired : null;
                fallback = shown.BsFallbackShare;
            }
        }
        else if (family == PairFamilies.Intraday)
        {
            current["version_key"] = PairFamilies.VersionKey;
            current["fill_engine"] = engine.EngineVersion;
            recorded["fill_engine"] = sources.GetValueOrDefault("fill_engine") ?? string.Empty;
            var charge = sources.GetValueOrDefault("charge_basis");
            basis = PairSheet(charge) is null ? "gross" : "net";
            recorded["cost_sheet"] = PairSheet(charge)?.Sha ?? "gross";
            current["cost_sheet"] = PairSheet(charge) is { } sheet ? context.SheetShas.GetValueOrDefault(sheet.Id, "unavailable") : "gross";
        }
        else
        {
            current["catalog"] = IvFeatureCatalog.Version;
            current["version_key"] = ExtensionCatalogs.GroupOf(IvFeatureCatalog.Version) is { } group ? ExtensionReport.VersionKeyOf(group) : "unavailable";
            if (record.ExtensionVersion is { } catalog)
            {
                recorded["catalog"] = catalog;
            }

            // The comparison states no charge basis unless its sources do; only a stored net lets a KEEP close the family.
            if (sources.GetValueOrDefault("charge_basis") == "net")
            {
                basis = "net";
            }

            // The features were read from the archive up to this fetch ("0" or "unavailable": the extraction read none).
            if (SourceWatermark(sources.GetValueOrDefault("archive_watermark")) is var mark and > 0)
            {
                watermark = new OptionsWatermarkFact(mark, context.Ended.TryGetValue(mark, out var at) ? at : null);
            }
        }

        return new OptionsRecordFact(record.EvaluationId, record.Protocol, record.Verdict, record.HoldOutRead, record.Trials, record.Rows, record.Missing, basis, recorded, current, watermark, sources.GetValueOrDefault("development_record"), derived, fallback);
    }

    // "sheet:{id}:{sha}" is the pairs study's charge basis; anything else is gross.
    private static (string Id, string Sha)? PairSheet(string? chargeBasis)
    {
        if (chargeBasis is null || !chargeBasis.StartsWith("sheet:", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = chargeBasis.Split(':');
        return parts.Length == 3 && parts[1].Length > 0 ? (parts[1], parts[2]) : null;
    }

    private static long SourceWatermark(string? text) => long.TryParse(text, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0L;

    private async ValueTask<DateTimeOffset?> LatestFetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await phase.LatestFetchEndedAtAsync(cancellationToken);
        }
#pragma warning disable CA1031 // An unreadable fetch log reads as no fetch, so every archive-based record is stale; cancellation is rethrown.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            OptionsPhaseCheckLog.Unreadable(logger, "fetch_log", failure.GetType().Name);
            return null;
        }
    }

    private static long Watermark(string inputsJson) => ReadInput(inputsJson, root => root.TryGetProperty("archive_watermark", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var id) ? id : 0L, 0L);

    private static string CostSheetOf(string inputsJson) => ReadInput(inputsJson, root => root.TryGetProperty("cost_sheet", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty, string.Empty);

    private static List<(Guid Underlying, string Model)> SpreadEntries(string inputsJson) => ReadInput(
        inputsJson,
        root =>
        {
            var list = new List<(Guid, string)>();
            if (root.TryGetProperty("spread", out var spread) && spread.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in spread.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String))
                {
                    // "{underlying:N}:{model id or none}:{assumed half-spread or none}"
                    var parts = (item.GetString() ?? string.Empty).Split(':');
                    if (parts.Length >= 2 && Guid.TryParseExact(parts[0], "N", out var id))
                    {
                        list.Add((id, parts[1]));
                    }
                }
            }

            return list;
        },
        []);

    private static T ReadInput<T>(string inputsJson, Func<JsonElement, T> read, T fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(inputsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object ? read(document.RootElement) : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private async ValueTask<OptionsCellFacts> CellsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rows = await phase.NonCashCellRowsAsync(OptionsPhaseCheck.MaxViolationRows + 1, cancellationToken);
            return new OptionsCellFacts(true, [.. rows.Take(OptionsPhaseCheck.MaxViolationRows).Select(r => new OptionsCellRow(r.Source, r.Id))], rows.Count > OptionsPhaseCheck.MaxViolationRows);
        }
#pragma warning disable CA1031 // Unreadable stored plans read as unavailable and keep the check from MET; cancellation is rethrown.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            OptionsPhaseCheckLog.Unreadable(logger, "cell_keys", failure.GetType().Name);
            return new OptionsCellFacts(false, [], false);
        }
    }

    private async ValueTask<OptionsArchiveFacts> ArchiveAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        return new OptionsArchiveFacts(await YearsAsync(now, cancellationToken), await UnderlyingsAsync(cancellationToken), await RecorderAsync(cancellationToken), await BackfillAsync(cancellationToken));
    }

    private async ValueTask<double?> YearsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var totals = await bars.GetBarTotalsAsync(now, cancellationToken);
            return totals.FirstBar is null || totals.LastBar is null ? 0d : OptionsPhaseCheck.YearsBetween(totals.FirstBar, totals.LastBar);
        }
#pragma warning disable CA1031 // Unreadable bars read as unavailable; cancellation is rethrown.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            OptionsPhaseCheckLog.Unreadable(logger, "bars", failure.GetType().Name);
            return null;
        }
    }

    private async ValueTask<IReadOnlyList<OptionsUnderlyingFact>?> UnderlyingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var summary = await archive.HandleAsync(new GetOptionArchiveSummaryQuery(), cancellationToken);
            return summary.IsFailure
                ? null
                : [.. summary.Value.Underlyings.Select(u => new OptionsUnderlyingFact(u.Underlying, u.UsableSessions.Value, u.Backfill.Done, u.Backfill.GivenUp))];
        }
#pragma warning disable CA1031 // An unreadable archive summary reads as unavailable; cancellation is rethrown.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            OptionsPhaseCheckLog.Unreadable(logger, "archive", failure.GetType().Name);
            return null;
        }
    }

    private async ValueTask<bool?> RecorderAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await phase.IsChainRecorderOnAsync(cancellationToken);
        }
#pragma warning disable CA1031 // An unreadable switch reads as unavailable; cancellation is rethrown.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            OptionsPhaseCheckLog.Unreadable(logger, "recorder_switch", failure.GetType().Name);
            return null;
        }
    }

    private async ValueTask<bool?> BackfillAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await phase.IsBackfillOnAsync(cancellationToken);
        }
#pragma warning disable CA1031 // An unreadable switch reads as unavailable; cancellation is rethrown.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            OptionsPhaseCheckLog.Unreadable(logger, "backfill_switch", failure.GetType().Name);
            return null;
        }
    }
}
