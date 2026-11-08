using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>
/// The liquidity tier and the capacity check of the out-of-sample trades of a Lab verdict (CF-05 D2 and D4, ADR 0223, ADR 0224): for each trade the 20-day median traded value from stored day bars, the order value (the costed entry turnover) against the 0.5 % cap of that
/// median, and against the 1 % cap of the traded value of the trade's fill minute, read from the one stored minute bar that starts at the entry (close x volume, the day bar's definition). A fill minute that cannot be read (over the run's bound, a failed or timed-out read, no stored bar,
/// no volume) reads unavailable and fails closed: it is never a pass. Two bounded reads and no others: the day bars once per instrument, and the fill minutes in batches per instrument, at most <see cref="CostStressOptions.FillMinuteMaxTrades"/> trades per run within
/// <see cref="CostStressOptions.FillMinuteTimeoutSeconds"/>. A failed read of one instrument makes its trades unavailable and does not stop the run (the block is report-only). Report-only: the result is carried on the cost stress block and read by the readiness scorecard's row 14 and the
/// report, and by nothing that judges a verdict. Only the Lab runner calls it, and this is the only caller of the minute read.
/// </summary>
internal sealed class LabTiers(IBarQueries bars, CostStressOptions options, TimeProvider time, ILogger<LabTiers> logger)
{
    /// <summary>What is measured and what is not read, printed with the figures.</summary>
    public const string Basis =
        "Measured on stored bars: the tier is the 20-day median of close x volume over the sessions before the entry date; the order (the entry turnover) is held against the capacity share of that median, and against the 1 % share of the traded value (close x volume) of the stored minute bar of its fill minute. A fill minute that is not read (over the run's bound, a failed or timed-out read, no stored bar or no volume) is unavailable, which counts as a fail, never as within capacity. The entry-day count is information only.";

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

        var fillMinutes = await ReadFillMinutesAsync(sources, series, cancellationToken);

        int a = 0, b = 0, c = 0, none = 0, breaches = 0, capacityNone = 0, overDay = 0, minuteReads = 0;
        foreach (var trade in sources)
        {
            if (series[trade.InstrumentId] is not { } dayBars)
            {
                none++;
                capacityNone++;
                continue;
            }

            decimal? fillValue = fillMinutes.TryGetValue((trade.InstrumentId, trade.EntryEpochSeconds), out var v) ? v : null;
            if (fillValue is not null)
            {
                minuteReads++;
            }

            var (tier, capacity, overEntryDay) = LiquidityTiers.Assess(trade.Costing?.EntryTurnover, dayBars, trade.TradeDate, options.TierACut, options.TierBCut, options.CapacityFillShare, options.CapacityDailyShare, fillValue);
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

        return new CostStressTiersView(sources.Count, a, b, c, none, breaches, capacityNone, options.TierKeyText(), options.TierSnapshotHash(), Basis, overDay, minuteReads, sources.Count - minuteReads, options.FillMinuteMaxTrades);
    }

    /// <summary>
    /// The traded value of the fill minute of the trades that can be judged (day bars read, order value known), by instrument and fill minute start. At most <see cref="CostStressOptions.FillMinuteMaxTrades"/> trades are read, taken in a fixed order (instrument, then entry time) so the same
    /// run reads the same trades; one batched read per instrument (up to <see cref="IBarQueries.MaxMinuteStartsPerRead"/> instants each); the run stops reading at the timeout. What is not in the result is unavailable.
    /// </summary>
    private async Task<Dictionary<(Guid InstrumentId, long EntryEpochSeconds), decimal>> ReadFillMinutesAsync(IReadOnlyList<SimTrade> sources, Dictionary<Guid, IReadOnlyList<DailyBar>?> series, CancellationToken cancellationToken)
    {
        var values = new Dictionary<(Guid, long), decimal>();
        var budget = options.FillMinuteMaxTrades;
        if (budget <= 0)
        {
            return values;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.FillMinuteTimeoutSeconds), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var judgeable = sources.Where(t => series[t.InstrumentId] is not null && t.Costing?.EntryTurnover is not null);
        foreach (var group in judgeable.GroupBy(t => t.InstrumentId).OrderBy(g => g.Key))
        {
            if (budget <= 0)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested)
            {
                LabRunLog.TiersMinutesTimeout(logger, options.FillMinuteTimeoutSeconds);
                break;
            }

            var requested = group.OrderBy(t => t.EntryEpochSeconds).Take(budget).ToList();
            budget -= requested.Count;
            var starts = requested.Select(t => t.EntryEpochSeconds).Distinct().Order().Select(DateTimeOffset.FromUnixTimeSeconds).ToList();
            try
            {
                foreach (var batch in starts.Chunk(IBarQueries.MaxMinuteStartsPerRead))
                {
                    var rows = await bars.ReadMinuteCloseVolumesAsync(group.Key, batch, linked.Token);
                    foreach (var row in rows)
                    {
                        if (LiquidityTiers.MinuteTradedValue(row.Close, row.Volume) is { } value)
                        {
                            values[(group.Key, row.Start.ToUnixTimeSeconds())] = value;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // out of time: this instrument and every later one stay unavailable, the run goes on
                LabRunLog.TiersMinutesTimeout(logger, options.FillMinuteTimeoutSeconds);
                break;
            }
#pragma warning disable CA1031 // the tier block is report-only: a failed minute read of one instrument makes its trades unavailable and must not fail the Lab run (cancellation still propagates)
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                if (timeout.IsCancellationRequested)
                {
                    // the read failed because the time ran out (a driver may wrap the cancellation): a timeout, not an unread instrument
                    LabRunLog.TiersMinutesTimeout(logger, options.FillMinuteTimeoutSeconds);
                    break;
                }

                LabRunLog.TiersMinutesUnread(logger, group.Key, requested.Count, ex);
            }
        }

        return values;
    }
}
