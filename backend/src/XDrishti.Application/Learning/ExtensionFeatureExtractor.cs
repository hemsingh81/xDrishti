using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Learning;

/// <summary>
/// Reads, for the trades of one instrument, everything the features of <c>ev-1</c> or <c>fu-1</c> need and hands it to the pure functions of the domain (P15-05, ADR 0185). Every read is at or before the decision instant of the trade it serves: the facts and the shareholding are asked
/// <b>as of</b> the instant, the filings and calendar rows are filtered by the point-in-time rule in the domain, and only closes of sessions that have closed by then are looked at. Facts are asked once per distinct set of visible filings of the instrument (they change only when a filing becomes
/// visible). Nothing is filled: an unknown value stays NaN with its reason.
/// <para>Timing note: the closes come from the 1D aggregate, about 1,300 rows an instrument over five years, where the minute bars are about 470,000; the full-universe fu-1 extraction reads thousands of rows, not ~100 million.</para>
/// </summary>
internal sealed class ExtensionFeatureExtractor(IBarQueries bars, IAdjustmentFactors adjustments, IResultFactsAsOf facts, IShareholdingAsOf shareholding)
{
    public async ValueTask<IReadOnlyDictionary<string, FeatureCell[]>> ExtractAsync(string version, Guid instrumentId, IReadOnlyList<ExtensionDecision> decisions, ExtensionSources sources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(sources);
        var result = new Dictionary<string, FeatureCell[]>(decisions.Count, StringComparer.Ordinal);
        if (decisions.Count == 0)
        {
            return result;
        }

        var filings = sources.Filings.GetValueOrDefault(instrumentId) ?? [];
        var factors = await adjustments.GetAsync(instrumentId, cancellationToken);
        var actions = factors.Actions;
        var ordered = decisions.OrderBy(d => d.At).ThenBy(d => d.TradeKey, StringComparer.Ordinal).ToList();
        var rows = sources.Snapshot.RowsOf(instrumentId);
        if (version == EventFeatureCatalog.Version)
        {
            var closes = filings.Count == 0 ? new Dictionary<DateOnly, double>() : ToMap(await ReadClosesAsync(instrumentId, ordered, cancellationToken));
            foreach (var d in ordered)
            {
                result[d.TradeKey] = EventFeatures.Compute(new EventFeatureInput(d.At, sources.Lag, sources.Calendar, rows, filings, closes, sources.IndexCloses, actions));
            }

            return result;
        }

        var list = await ReadClosesAsync(instrumentId, ordered, cancellationToken);
        var cache = new Dictionary<int, ResultFactsAnswer>();
        foreach (var d in ordered)
        {
            var visible = filings.Count(f => f.BroadcastTs + sources.Lag <= d.At);
            if (!cache.TryGetValue(visible, out var answer))
            {
                answer = await facts.Facts(instrumentId, d.At, cancellationToken);
                cache[visible] = answer;
            }

            var holding = await shareholding.Latest(instrumentId, d.At, cancellationToken);
            result[d.TradeKey] = FundamentalFeatures.Compute(new FundamentalFeatureInput(d.At, sources.Lag, sources.Calendar, answer, holding, actions, list));
        }

        return result;
    }

    // The raw close of every complete day bar (market.bars_agg 1D) from the first stored day up to the date of the last decision: a few thousand rows an instrument, never the minute bars. Later dates are never asked for;
    // an incomplete day (a truncated tape) is left out rather than given a doubtful close.
    private async ValueTask<List<DailyClose>> ReadClosesAsync(Guid instrumentId, List<ExtensionDecision> ordered, CancellationToken cancellationToken)
    {
        var last = SessionClock.DateOf(ordered[^1].At.ToUnixTimeSeconds());
        var days = await bars.ReadDailyBarsAsync(instrumentId, new DateOnly(2000, 1, 1), last.AddDays(1), cancellationToken);
        return [.. days.Where(d => d.Complete).Select(d => new DailyClose(IndiaTime.Today(d.Ts), d.Close)).OrderBy(c => c.Date)];
    }

    private static Dictionary<DateOnly, double> ToMap(List<DailyClose> closes)
    {
        var map = new Dictionary<DateOnly, double>(closes.Count);
        foreach (var close in closes)
        {
            map[close.Date] = close.Close;
        }

        return map;
    }
}
