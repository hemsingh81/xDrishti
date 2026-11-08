using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Readiness;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The forward (shadow and paper) trades of a strategy counted by liquidity tier, for the hard fail "evidence only from tier C" (CF-11 D3, ADR 0225). Derived on every request, nothing stored: each graded forward trade of the current epoch is given the tier of its instrument by the pure
/// <see cref="LiquidityTiers"/> rules, from the day bars before its entry session (the same 20-day median and the same cuts as the Lab's tier block, ADR 0223). A symbol that does not resolve to exactly one share, a failed bar read or fewer than 20 usable sessions make a trade's tier
/// unavailable (never tier C). One bounded read of day bars per instrument. Read only.
/// </summary>
internal sealed class ForwardTierReader(ForwardEdgeReader edge, IInstrumentQueries instruments, IBarQueries bars, CostStressOptions options, ILogger<ForwardTierReader> logger)
{
    /// <summary>The name of the forward set beside the hard fail.</summary>
    public const string SetName = "forward shadow and paper trades";

    /// <summary>Calendar days read before the first entry of an instrument: 20 sessions fit in about 28 calendar days; the rest is room for holidays and halts (as the Lab's tier block).</summary>
    private const int LookBackDays = 60;

    /// <summary>The day bars already read in this scope, per instrument, with the range they cover; another strategy asking for a range inside it reuses them (as <see cref="ForwardEdgeReader"/> keeps its rows). A failed read is not kept.</summary>
    private readonly Dictionary<Guid, (DateOnly From, DateOnly To, IReadOnlyList<DailyBar> Series)> _barCache = [];

    /// <summary>The forward set of the strategy; a read that fails (the shadow rows, the master list) is an unavailable set, never a pass and never tier C.</summary>
    public async ValueTask<TierSet> ReadAsync(string strategy, CancellationToken cancellationToken)
    {
        try
        {
            return await DeriveAsync(strategy, cancellationToken);
        }
#pragma warning disable CA1031 // report-only: an unreadable source makes the set unavailable, which counts as a fail (cancellation still propagates)
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            ReadinessLog.ForwardTiersUnreadable(logger, ex.GetType().Name);
            return TierSet.Unreadable(SetName, "the forward trades or the day bars could not be read (" + ex.GetType().Name + ")");
        }
    }

    private async ValueTask<TierSet> DeriveAsync(string strategy, CancellationToken cancellationToken)
    {
        var trades = await edge.GradedTradesAsync(strategy, cancellationToken);
        if (trades.Count == 0)
        {
            return TierSet.Of(SetName, new TierCounts(0, 0, 0, 0));
        }

        var named = trades.Where(t => !string.IsNullOrWhiteSpace(t.Symbol)).ToList();
        var ids = await ResolveAsync([.. named.Select(t => t.Symbol!.ToUpperInvariant()).Distinct(StringComparer.Ordinal)], cancellationToken);
        int a = 0, b = 0, c = 0, none = trades.Count - named.Count;
        foreach (var group in named.GroupBy(t => t.Symbol!.ToUpperInvariant(), StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var series = ids.TryGetValue(group.Key, out var id) ? await DayBarsAsync(id, group.ToList(), cancellationToken) : null;
            if (series is null)
            {
                none += group.Count();
                continue;
            }

            foreach (var trade in group)
            {
                switch (LiquidityTiers.Assess(null, series, trade.Session, options.TierACut, options.TierBCut, options.CapacityFillShare, options.CapacityDailyShare).Tier)
                {
                    case LiquidityTier.A:
                        a++;
                        break;
                    case LiquidityTier.B:
                        b++;
                        break;
                    case LiquidityTier.C:
                        c++;
                        break;
                    default:
                        none++;
                        break;
                }
            }
        }

        return TierSet.Of(SetName, new TierCounts(a, b, c, none));
    }

    /// <summary>The symbols that name exactly one share of the master list (retired ones included: the trade happened); any other symbol is left out.</summary>
    private async ValueTask<Dictionary<string, Guid>> ResolveAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (symbols.Count == 0)
        {
            return result;
        }

        var found = await instruments.FindForResolveAsync(symbols, [], cancellationToken);
        foreach (var group in found.Where(f => f.Type == InstrumentTypeCodes.Share).GroupBy(f => f.Symbol.ToUpperInvariant(), StringComparer.Ordinal))
        {
            var candidates = group.ToList();
            if (candidates.Count == 1)
            {
                result[group.Key] = candidates[0].Id;
            }
        }

        return result;
    }

    private async ValueTask<IReadOnlyList<DailyBar>?> DayBarsAsync(Guid instrumentId, List<ForwardTrade> trades, CancellationToken cancellationToken)
    {
        var from = trades.Min(t => t.Session).AddDays(-LookBackDays);
        var to = trades.Max(t => t.Session).AddDays(1);
        if (_barCache.TryGetValue(instrumentId, out var hit) && hit.From <= from && hit.To >= to)
        {
            return hit.Series;
        }

        try
        {
            // shortcut: one read per instrument, in series, over the range of this scope's trades (widened to the union with an earlier read); read the instruments in parallel or per month when the forward record grows.
            if (hit.Series is not null)
            {
                from = from < hit.From ? from : hit.From;
                to = to > hit.To ? to : hit.To;
            }

            var rows = await bars.ReadDailyBarsAsync(instrumentId, from, to, cancellationToken);
            IReadOnlyList<DailyBar> series = [.. rows.Select(r => new DailyBar(IndiaTime.Today(r.Ts), r.Open, r.High, r.Low, r.Close, r.Volume))];
            _barCache[instrumentId] = (from, to, series);
            return series;
        }
#pragma warning disable CA1031 // report-only: a failed read of one instrument's bars makes its trades tier-unavailable and the set unavailable, never a pass (cancellation still propagates)
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            ReadinessLog.ForwardBarsUnread(logger, ex.GetType().Name, trades.Count);
            return null;
        }
    }
}
