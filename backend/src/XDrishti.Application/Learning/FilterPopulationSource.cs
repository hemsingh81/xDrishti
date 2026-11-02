using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Learning;

/// <summary>
/// The population of protocol F (P15-10): the closed trades of every stored run that did not itself use the event filter (its stored record has no <c>events=</c> line, which only a run that names <c>not_event_day</c> has), one per trade key (the newest engine wins), each with the calendar's answer at its decision instant from one snapshot
/// read. Gross R from the filled prices. Reads only.
/// </summary>
internal sealed class FilterPopulationSource(IRunStore runs, IEventCalendarSnapshots snapshots, ITradingCalendar calendars, IFilingSettings settings) : IFilterPopulationSource
{
    // The run record is canonical text, one "name=value" line each; a run that names not_event_day has an "events=" line (RunAssembler, P15-06) and no other run has.
    private const string EventLine = "events=";

    public async ValueTask<Result<FilterPopulation>> ReadAsync(CancellationToken cancellationToken)
    {
        var headers = new List<StoredRunHeader>();
        string? cursor = null;
        do
        {
            var page = await runs.ListAsync(new RunListFilter(), new PageRequest(PageRequest.MaxSize, cursor), cancellationToken);
            if (page.IsFailure)
            {
                return Result.Failure<FilterPopulation>(page.Error);
            }

            headers.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        var skipped = 0;
        var used = new List<StoredRunHeader>();
        foreach (var header in headers.OrderBy(h => h.RunKey, StringComparer.Ordinal))   // checked in key order, then ordered below
        {
            var text = await runs.ReadRecordTextAsync(header.RunKey, cancellationToken);
            if (text is null || text.Split('\n').Any(line => line.StartsWith(EventLine, StringComparison.Ordinal)))
            {
                skipped++;
                continue;
            }

            used.Add(header);
        }

        // A trade key can be in several runs (a re-run of the strategy): the newest engine wins, then the newest run, then the smaller key, so the choice is the same on every read.
        used = [.. used.OrderByDescending(h => EngineRank(h.EngineVersion)).ThenByDescending(h => h.EngineVersion, StringComparer.Ordinal).ThenByDescending(h => h.CreatedAt).ThenBy(h => h.RunKey, StringComparer.Ordinal)];
        var trades = new Dictionary<string, (Guid Instrument, DateTimeOffset At, DateOnly Date, double R)>(StringComparer.Ordinal);
        foreach (var header in used)
        {
            await foreach (var runTrade in runs.StreamTradesAsync(header.RunKey, false, cancellationToken))
            {
                var t = runTrade.Trade;
                trades.TryAdd(t.Key, (t.InstrumentId, DateTimeOffset.FromUnixTimeSeconds(t.DecisionEpochSeconds), t.TradeDate, t.RFilled));
            }
        }

        var runKeys = used.Select(h => h.RunKey).Order(StringComparer.Ordinal).ToArray();
        var engines = used.Select(h => h.EngineVersion).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (trades.Count == 0)
        {
            return Result.Success(new FilterPopulation([], "none", settings.LagMinutes, runKeys, engines, skipped));
        }

        var ids = trades.Values.Select(v => v.Instrument).Distinct().Order().ToList();
        var snapshot = await snapshots.ReadAsync(ids, trades.Values.Max(v => IndiaTime.Today(v.At)), cancellationToken);
        if (snapshot.IsFailure)
        {
            return Result.Failure<FilterPopulation>(snapshot.Error);
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var observations = trades
            .OrderBy(p => p.Value.Date).ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new FilterObservation(p.Key, p.Value.Date, p.Value.R, snapshot.Value.Answer(p.Value.Instrument, p.Value.At, calendar)))
            .ToArray();
        return Result.Success(new FilterPopulation(observations, snapshot.Value.Sha256, snapshot.Value.LagMinutes, runKeys, engines, skipped));
    }

    // sim-10 is newer than sim-9: compare the number after the last dash, not the text.
    private static int EngineRank(string engine)
    {
        var dash = engine.LastIndexOf('-');
        return dash >= 0 && int.TryParse(engine.AsSpan(dash + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : -1;
    }
}
