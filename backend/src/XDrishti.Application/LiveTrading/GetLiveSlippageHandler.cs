using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.LiveTrading;

/// <summary>The stored slippage estimates of a week (the newest when none is given): one row per bucket, side, style and reference kind, each with its n; <c>empty</c> when nothing is stored. Read only.</summary>
internal sealed class GetLiveSlippageHandler(ILiveExecutionStore store, ILiveEstimateSettings settings) : IQueryHandler<LiveSlippageQuery, Result<LiveSlippageView>>
{
    public async ValueTask<Result<LiveSlippageView>> HandleAsync(LiveSlippageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.WeekStart is { } w && w.DayOfWeek != DayOfWeek.Monday)
        {
            return Result.Failure<LiveSlippageView>(Error.Validation(LiveErrors.Invalid, "weekStart must be a Monday"));
        }

        var (week, rows) = await store.SlippageEstimatesAsync(query.WeekStart, cancellationToken);
        var summary = week is { } start ? await store.ExecutionSummaryAsync(start.AddDays(6), cancellationToken) : (Count: 0, First: (DateOnly?)null, Last: (DateOnly?)null);
        var evidence = LiveExecutionMapping.Evidence("total slippage in basis points of the entry, by liquidity bucket", rows.Sum(r => r.N), summary.First, summary.Last, settings.Edges);
        return Result.Success(new LiveSlippageView(week, rows.Count == 0 ? "empty" : "value", [.. rows.Select(Row)], evidence));
    }

    private static LiveSlippageRowView Row(NewSlippageEstimate r)
    {
        MetricView M(double? v, string missing) => LiveExecutionMapping.Metric(r.State, v, r.Reason, missing);
        return new LiveSlippageRowView(r.Bucket, r.Side, r.Style, r.ReferenceKind, r.N, M(r.MeanBps, "no mean"), M(r.MeanLo, "no interval"), M(r.MeanHi, "no interval"), M(r.MedianBps, "no median"), M(r.MedianLo, "no median interval"), M(r.MedianHi, "no median interval"), M(r.MeanTicks, "the tick is not known for these entries"), M(r.MeanR, "the stop is not known for these entries"), r.State, r.Proposal);
    }
}
