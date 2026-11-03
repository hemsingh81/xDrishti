using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Selection;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.LearningPhase;

/// <summary>
/// The P15 phase check (spec P15-12): reads the stored verdicts of the five families, the event-filter decision history, the filings and calendar coverage and the seasons of bars through <b>read ports only</b> and hands plain facts to the pure
/// <see cref="EventsPhaseCheck"/>. It writes nothing, calls no model and places no order. A source that cannot be read (a missing table included) makes its family NOT EVALUABLE or its figure <c>unavailable</c>; it is never an error of the call and never a MET.
/// </summary>
internal sealed class GetEventsPhaseCheckHandler(
    IExtensionEvaluationReader evaluations,
    IEventFilterHistory history,
    IFilingReader filings,
    IFilingInstruments instruments,
    IResultsCalendar calendar,
    IFilingSettings settings,
    IBarQueries bars,
    ISimulationEngineVersion engine,
    TimeProvider clock,
    ILogger<GetEventsPhaseCheckHandler> logger) : IQueryHandler<EventsPhaseCheckQuery, Result<EventsPhaseCheckView>>
{
    private const int RecordsRead = 100;

    public async ValueTask<Result<EventsPhaseCheckView>> HandleAsync(EventsPhaseCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var unknown = query.Cut.Where(c => !ExtensionFamilies.IsKnown(c)).ToList();
        if (unknown.Count > 0)
        {
            return Result.Failure<EventsPhaseCheckView>(Error.Validation("events.unknown_family", "Unknown family in --cut: " + string.Join(", ", unknown.Select(Printable)) + ". Known: " + string.Join(", ", ExtensionFamilies.All) + "."));
        }

        var now = clock.GetUtcNow();
        var lag = settings.LagMinutes.ToString(CultureInfo.InvariantCulture);

        var filter = await ReadFilterAsync(cancellationToken);
        var families = new List<EventsFamilyFacts>();
        double? share = null;
        string? shareRecord = null;
        foreach (var family in ExtensionFamilies.All)
        {
            var read = await ReadFamilyAsync(family, filter.Days, lag, cancellationToken);
            families.Add(read.Facts);
            if (family == ExtensionFamilies.Filter)
            {
                (share, shareRecord) = read.Share;
            }
        }

        var (tracked, withEntry) = await CalendarAsync(now, cancellationToken);
        var report = EventsPhaseCheck.Evaluate(
            new EventsPhaseFacts(families, filter, await SeasonsAsync(now, cancellationToken), await FilingsAsync(now, cancellationToken), tracked, withEntry, share, shareRecord),
            query.Cut);
        EventsPhaseCheckLog.Evaluated(logger, report.Verdict, report.Open.Count, report.Violations.Count, report.Cut.Count);
        return Result.Success(new EventsPhaseCheckView(report, report.ToText(), report.ToJson()));
    }

    private static string Printable(string text) => new([.. text.Take(40).Select(ch => char.IsControl(ch) ? '?' : ch)]);

    private async ValueTask<EventsFilterFact> ReadFilterAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rows = await history.RowsAsync(EventFilterSettings.HistorySize, cancellationToken);
            var state = EventFilterSettings.StateOf(rows);
            return new EventsFilterFact(true, state.Enabled, state.ResultsWithinDays, rows.Count > 0 ? rows[0].Id : null, state.DecidedBy);
        }
#pragma warning disable CA1031 // An unreadable history reads as OFF and says so (ADR 0182); cancellation is rethrown.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            EventsPhaseCheckLog.Unreadable(logger, "event_filter_history", failure.GetType().Name);
            return new EventsFilterFact(false, false, EventFilterState.Off.ResultsWithinDays, null, null);
        }
    }

    private async ValueTask<(EventsFamilyFacts Facts, (double? Share, string? Record) Share)> ReadFamilyAsync(string family, int days, string lag, CancellationToken cancellationToken)
    {
        var current = CurrentVersions(family, lag);
        IReadOnlyList<ExtensionEvaluationRecord> stored;
        try
        {
            stored = await evaluations.ListAsync(family, RecordsRead, cancellationToken);
        }
#pragma warning disable CA1031 // A store that cannot be read (a missing table included) makes the family NOT EVALUABLE; cancellation is rethrown.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            EventsPhaseCheckLog.Unreadable(logger, family, failure.GetType().Name);
            return (new EventsFamilyFacts(family, false, [], current), (null, null));
        }

        (double? Share, string? Record) share = (null, null);
        IEnumerable<ExtensionEvaluationRecord> chosen;
        switch (family)
        {
            case ExtensionFamilies.Filter:
                var all = stored.Where(r => r.Protocol == ProtocolF.Protocol).ToList();
                if (all.Count > 0)
                {
                    share = (ShareOf(all[0].EstimatesJson), all[0].EvaluationId);
                }

                var key = string.Create(CultureInfo.InvariantCulture, $":N={days}");
                chosen = all.Where(r => r.VersionKey.EndsWith(key, StringComparison.Ordinal));
                break;
            case ExtensionFamilies.Flag:
                chosen = stored.Where(r => r.Protocol is "L" or "X");
                break;
            case ExtensionFamilies.Pead:
                chosen = stored.Where(r => r.Protocol == "P");
                break;
            default:
                chosen = stored.Where(r => r.Protocol == "X");
                break;
        }

        return (new EventsFamilyFacts(family, true, [.. chosen.Select(Fact)], current), share);
    }

    private Dictionary<string, string> CurrentVersions(string family, string lag) => family switch
    {
        ExtensionFamilies.Filter => new() { ["rule_version"] = EventCalendarSnapshot.RuleVersion, ["lag_minutes"] = lag },
        ExtensionFamilies.EventFeatures => new() { ["catalog"] = EventFeatureCatalog.Version, ["lag_minutes"] = lag },
        ExtensionFamilies.FundamentalFeatures => new() { ["catalog"] = FundamentalFeatureCatalog.Version, ["lag_minutes"] = lag, ["parser_version"] = ResultsParser.Version },
        ExtensionFamilies.Flag => new() { ["catalog"] = LlmFeatureCatalog.Version },
        _ => new() { ["engine"] = engine.EngineVersion },
    };

    private static EventsRecordFact Fact(ExtensionEvaluationRecord record)
    {
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        if (record.ExtensionVersion is { } catalog)
        {
            versions["catalog"] = catalog;
        }

        try
        {
            using var document = JsonDocument.Parse(record.InputsJson);
            if (document.RootElement.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in sources.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                {
                    versions[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            // Unreadable inputs record no version: every compared key then reads as not recorded, so the record is stale, never current.
        }

        return new EventsRecordFact(record.EvaluationId, record.Protocol, record.Verdict, record.HoldOutRead, record.Trials, record.Rows, record.Missing, versions);
    }

    private static double? ShareOf(string estimatesJson)
    {
        try
        {
            using var document = JsonDocument.Parse(estimatesJson);
            return document.RootElement.TryGetProperty("share_lost_to_no_upcoming_entry", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var share) && double.IsFinite(share) ? share : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async ValueTask<int?> SeasonsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var totals = await bars.GetBarTotalsAsync(now, cancellationToken);
            if (totals.FirstBar is not { } first || totals.LastBar is not { } last)
            {
                return 0;
            }

            var from = Season.Of(IndiaTime.Today(first));
            var to = Season.Of(IndiaTime.Today(last));
            return Math.Max(0, ((to.Year - from.Year) * 4) + (to.Quarter - from.Quarter) + 1);
        }
#pragma warning disable CA1031 // Unreadable bars read as unavailable; cancellation is rethrown.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            EventsPhaseCheckLog.Unreadable(logger, "bars", failure.GetType().Name);
            return null;
        }
    }

    private async ValueTask<IReadOnlyList<EventsKindFact>?> FilingsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var data = await filings.CoverageAsync(new DateOnly(2000, 1, 1), IndiaTime.Today(now), cancellationToken);
            return [.. Enum.GetValues<FilingKind>().Select(kind =>
            {
                var code = FilingKinds.Code(kind);
                var cells = data.Cells.Where(c => c.Kind == code).ToList();
                return new EventsKindFact(code, cells.Sum(c => c.Ok), cells.Count(c => c.Ok > 0), data.InstrumentsWithout.GetValueOrDefault(code, data.TrackedInstruments));
            })];
        }
#pragma warning disable CA1031 // Unreadable coverage reads as unavailable; cancellation is rethrown.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            EventsPhaseCheckLog.Unreadable(logger, "filings", failure.GetType().Name);
            return null;
        }
    }

    private async ValueTask<(int? Tracked, int? WithEntry)> CalendarAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var all = await instruments.TrackedAsync(cancellationToken);
            var answers = all.Count == 0 ? new Dictionary<Guid, ResultsAnswer>() : new Dictionary<Guid, ResultsAnswer>(await calendar.DaysToResults([.. all.Select(i => i.Id)], now, cancellationToken));
            return (all.Count, all.Count(i => answers.TryGetValue(i.Id, out var a) && ResultsWindow.DaysOrNull(a) is not null));
        }
#pragma warning disable CA1031 // An unreadable calendar reads as unavailable; cancellation is rethrown.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            EventsPhaseCheckLog.Unreadable(logger, "calendar", failure.GetType().Name);
            return (null, null);
        }
    }
}
