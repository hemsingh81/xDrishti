using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.LiveTrading;

/// <summary>The stored capacity scores of a week (the newest when none is given): the share of orders filled in full for the account and each bucket; <c>empty</c> when nothing is stored. Display only; selection does not read it. Read only.</summary>
internal sealed class GetLiveCapacityHandler(ILiveExecutionStore store, ILiveEstimateSettings settings) : IQueryHandler<LiveCapacityQuery, Result<LiveCapacityView>>
{
    public async ValueTask<Result<LiveCapacityView>> HandleAsync(LiveCapacityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.WeekStart is { } w && w.DayOfWeek != DayOfWeek.Monday)
        {
            return Result.Failure<LiveCapacityView>(Error.Validation(LiveErrors.Invalid, "weekStart must be a Monday"));
        }

        var (week, rows) = await store.CapacityScoresAsync(query.WeekStart, cancellationToken);
        var summary = week is { } start ? await store.ExecutionSummaryAsync(start.AddDays(6), cancellationToken) : (Count: 0, First: (DateOnly?)null, Last: (DateOnly?)null);
        var evidence = LiveExecutionMapping.Evidence("share of entry orders filled in full (full, partial, rejected and queue-miss orders are counted)", rows.Where(r => r.Scope == LiveEstimateMath.AccountScope).Sum(r => r.NOrders), summary.First, summary.Last, settings.Edges);
        return Result.Success(new LiveCapacityView(week, rows.Count == 0 ? "empty" : "value", [.. rows.Select(Row)], evidence));
    }

    private static LiveCapacityRowView Row(NewCapacityScore r)
    {
        MetricView M(double? v) => LiveExecutionMapping.Metric(r.State, v, r.Reason, "no score");
        return new LiveCapacityRowView(r.Scope, r.ScopeKey, r.NOrders, r.NFull, r.NPartial, r.NRejected, r.NQueueMiss, r.NPriceMiss, r.NUnclassified, M(r.Score), M(r.Lo), M(r.Hi), r.State);
    }
}
