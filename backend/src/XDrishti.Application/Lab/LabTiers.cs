using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>
/// The liquidity tier and the capacity check of the out-of-sample trades of a Lab verdict (CF-05 D2 and D4, ADR 0223): for each trade the 20-day median traded value from stored day bars and the order value (the costed entry turnover) against the 0.5 % cap of that median.
/// The 1 % cap of the fill minute's traded value is not read (no minute bar is read), so it reads unavailable and fails closed: without a breach no capacity reading is a pass. One bounded read of the day bars per instrument; a failed read makes that instrument's trades
/// unavailable and does not stop the run (the block is report-only). Report-only: the result is carried on the cost stress block and read by the readiness scorecard's row 14 and the report, and by nothing that judges a verdict. Only the Lab runner calls it.
/// </summary>
internal sealed class LabTiers(IBarQueries bars, CostStressOptions options, ILogger<LabTiers> logger)
{
    /// <summary>What is measured and what is not read, printed with the figures.</summary>
    public const string Basis =
        "Measured on day bars: the tier is the 20-day median of close x volume over the sessions before the entry date, and the order (the entry turnover) is held against the capacity share of that median. NOT read: the fill minute's traded value, so its 1 % cap is unavailable (fail closed) and no order counts as within capacity until a later slice reads it. The entry-day count is information only.";

    /// <summary>Calendar days read before the first entry of an instrument: 20 sessions fit in about 28 calendar days; the rest is room for holidays and halts.</summary>
    private const int LookBackDays = 60;

    /// <summary>True when the tiers are computed (the cost stress is on).</summary>
    public bool IsOn => options.IsOn;

    /// <param name="sources">The simulated trades behind the out-of-sample trades (costed: <see cref="TradeCosting.EntryTurnover"/> is the order value).</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async ValueTask<CostStressTiersView?> ComputeAsync(IReadOnlyList<SimTrade>? sources, CancellationToken cancellationToken)
    {
        if (!options.IsOn || sources is null)
        {
            return null;
        }

        var series = new Dictionary<Guid, IReadOnlyList<DailyBar>?>();
        foreach (var group in sources.GroupBy(t => t.InstrumentId).OrderBy(g => g.Key))
        {
            var first = group.Min(t => t.TradeDate);
            var last = group.Max(t => t.TradeDate);
            try
            {
                var rows = await bars.ReadDailyBarsAsync(group.Key, first.AddDays(-LookBackDays), last.AddDays(1), cancellationToken);
                series[group.Key] = [.. rows.Select(r => new DailyBar(IndiaTime.Today(r.Ts), r.Open, r.High, r.Low, r.Close, r.Volume))];
            }
#pragma warning disable CA1031 // the tier block is report-only: a failed read of one instrument's bars makes its trades unavailable and must not fail the Lab run (cancellation still propagates)
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LabRunLog.TiersBarsUnread(logger, group.Key, group.Count(), ex);
                series[group.Key] = null;
            }
        }

        int a = 0, b = 0, c = 0, none = 0, breaches = 0, capacityNone = 0, overDay = 0;
        foreach (var trade in sources)
        {
            if (series[trade.InstrumentId] is not { } dayBars)
            {
                none++;
                capacityNone++;
                continue;
            }

            var (tier, capacity, overEntryDay) = LiquidityTiers.Assess(trade.Costing?.EntryTurnover, dayBars, trade.TradeDate, options.TierACut, options.TierBCut, options.CapacityFillShare, options.CapacityDailyShare);
            switch (tier)
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

            if (overEntryDay == true)
            {
                overDay++;
            }

            if (!capacity.Breach)
            {
                continue;
            }

            // a breach with an "unavailable" reason is a reading that could not be made, not an order that is too big
            if (capacity.Reason.StartsWith("unavailable", StringComparison.Ordinal))
            {
                capacityNone++;
            }
            else
            {
                breaches++;
            }
        }

        return new CostStressTiersView(sources.Count, a, b, c, none, breaches, capacityNone, options.TierKeyText(), options.TierSnapshotHash(), Basis, overDay);
    }
}
