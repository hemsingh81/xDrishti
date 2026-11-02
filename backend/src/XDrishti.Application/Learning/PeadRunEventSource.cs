using System.Globalization;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning;

/// <summary>
/// The events of protocol P (P15-09, ADR 0186): the trades of the newest stored runs of the strategy <c>PEAD</c> in its four variants (measure times hold), top bucket and middle three as the control. Each variant and bucket is found by the definition hash the engine gives for its parameters,
/// so a run of another strategy or of other parameters is never read; the newest run of the current engine version wins, and the runs of an older engine are left out. A trade becomes an event with its entry date, the sessions of its entry and exit counted in the trading calendar and its gross
/// R from the filled prices. When no run is stored the answer is an empty set with the reason, and protocol P states NOT_EVALUABLE; when the runs hold too few seasons it states the seasons. Reads only: it runs nothing and stores nothing.
/// </summary>
internal sealed class PeadRunEventSource(IRunStore runs, ISimulationEngine engine, ITradingCalendar calendars) : IPeadEventSource
{
    private const string Strategy = "PEAD";

    private const int PageSize = 200;

    // measure, hold and the label of the variant in the order of ProtocolP.Variants (the label is checked against it by a test)
    private static readonly (double Measure, double Hold, string Variant)[] Variants = [(1, 5, "sue_yoy/5"), (1, 20, "sue_yoy/20"), (2, 5, "results_car3/5"), (2, 20, "results_car3/20")];

    public async ValueTask<PeadEventSet> ReadAsync(CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<string, (string Variant, PeadBucket Bucket)>(StringComparer.Ordinal);
        foreach (var (measure, hold, variant) in Variants)
        {
            foreach (var (code, bucket) in new[] { (1.0, PeadBucket.Top), (2.0, PeadBucket.Middle) })
            {
                var prepared = engine.Prepare(Strategy, new Dictionary<string, double> { ["measure"] = measure, ["hold"] = hold, ["buckets"] = code }, Timeframe.D1, null);
                if (prepared.IsSuccess)
                {
                    hashes[prepared.Value.Record.DefinitionSha256] = (variant, bucket);
                }
            }
        }

        if (hashes.Count == 0)
        {
            return new PeadEventSet([], "no PEAD events: the engine does not know the strategy PEAD");
        }

        // newest first. A set is the eight runs (four variants, top and middle) that share the window, the universe, the fills and the costs; the newest run of each (variant, bucket) within a set is read.
        // The hash of the surprise numbers is in the run key but not in the stored header, so two sets that differ only in it cannot be told apart here; the key of each run is in the note.
        var sets = new Dictionary<string, Dictionary<(string, PeadBucket), (StoredRunHeader Header, int Order)>>(StringComparer.Ordinal);
        string? cursor = null;
        var order = 0;
        do
        {
            var page = await runs.ListAsync(new RunListFilter(Strategy, engine.EngineVersion), new PageRequest(PageSize, cursor), cancellationToken);
            if (page.IsFailure)
            {
                return new PeadEventSet([], "no PEAD events: the stored runs could not be listed (" + page.Error.Code + ")");
            }

            foreach (var header in page.Value.Items)
            {
                order++;
                if (hashes.TryGetValue(header.DefinitionSha256, out var which))
                {
                    var key = string.Join('|', header.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), header.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), header.UniverseKind, header.UniverseLabel, header.Instruments.ToString(CultureInfo.InvariantCulture), header.Fills, header.CostsSheetId ?? "-", header.CostsRatesSha256 ?? "-");
                    if (!sets.TryGetValue(key, out var members))
                    {
                        sets[key] = members = [];
                    }

                    members.TryAdd(which, (header, order));
                }
            }

            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        if (sets.Count == 0)
        {
            return new PeadEventSet([], "no PEAD events: no stored run of PEAD for engine " + engine.EngineVersion + " (run the four variants and the control, then evaluate)");
        }

        var complete = sets.Values.Where(m => m.Count == hashes.Count).OrderBy(m => m.Values.Min(v => v.Order)).FirstOrDefault();
        if (complete is null)
        {
            return new PeadEventSet([], "no coherent set of the eight PEAD runs: no group of runs shares the window, universe, fills and costs for all four variants and both buckets");
        }

        var chosen = complete.ToDictionary(c => c.Key, c => c.Value.Header);

        var trades = new List<(string Variant, PeadBucket Bucket, SimTrade Trade)>();
        foreach (var ((variant, bucket), header) in chosen.OrderBy(c => c.Key.Item1, StringComparer.Ordinal).ThenBy(c => c.Key.Item2))
        {
            await foreach (var runTrade in runs.StreamTradesAsync(header.RunKey, false, cancellationToken))
            {
                trades.Add((variant, bucket, runTrade.Trade));
            }
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var events = new List<PeadEvent>(trades.Count);
        if (trades.Count > 0)
        {
            var first = trades.Min(t => t.Trade.TradeDate).AddDays(-1);
            var last = trades.Max(t => SessionClock.DateOf(t.Trade.ExitEpochSeconds));
            var index = new Dictionary<DateOnly, int>();
            var count = 0;
            for (var date = first; date <= last; date = date.AddDays(1))
            {
                if (calendar.IsTradingDay(date))
                {
                    index[date] = ++count;
                }
            }

            foreach (var (variant, bucket, trade) in trades)
            {
                var exit = SessionClock.DateOf(trade.ExitEpochSeconds);
                if (!index.TryGetValue(trade.TradeDate, out var entrySession) || !index.TryGetValue(exit, out var exitSession) || !double.IsFinite(trade.RFilled))
                {
                    continue;
                }

                events.Add(new PeadEvent(variant + "|" + bucket + "|" + trade.Key, variant, trade.TradeDate, entrySession, Math.Max(entrySession, exitSession), bucket, trade.RFilled));
            }
        }

        var note = string.Create(CultureInfo.InvariantCulture, $"{chosen.Count} stored runs of PEAD for engine {engine.EngineVersion}: {string.Join(", ", chosen.OrderBy(c => c.Key.Item1, StringComparer.Ordinal).ThenBy(c => c.Key.Item2).Select(c => c.Key.Item1 + "/" + c.Key.Item2 + "=" + c.Value.RunKey[..12]))}");
        return new PeadEventSet(events, note);
    }
}
