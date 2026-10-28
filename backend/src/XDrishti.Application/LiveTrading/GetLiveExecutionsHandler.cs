using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>The stored executions of live entries in a range of sessions, newest first, one page (spec P11-02). Defaults: the last 30 days, size 50; at most 92 days and 200 rows a page. Read only.</summary>
internal sealed class GetLiveExecutionsHandler(ILiveExecutionStore store, ILiveEstimateSettings settings, TimeProvider time) : IQueryHandler<LiveExecutionsQuery, Result<LiveExecutionsPage>>
{
    public const int MaxRangeDays = 92;
    public const int MaxSize = 200;

    public async ValueTask<Result<LiveExecutionsPage>> HandleAsync(LiveExecutionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var today = IndiaTime.Today(time.GetUtcNow());
        var to = query.To ?? today;
        var from = query.From ?? to.AddDays(-30);
        if (from > to)
        {
            return Result.Failure<LiveExecutionsPage>(Error.Validation(LiveErrors.Invalid, "from must not be after to"));
        }

        if (to.DayNumber - from.DayNumber > MaxRangeDays)
        {
            return Result.Failure<LiveExecutionsPage>(Error.Validation(LiveErrors.Invalid, $"the range must be at most {MaxRangeDays} days"));
        }

        if (query.Page < 1 || query.Size is < 1 or > MaxSize)
        {
            return Result.Failure<LiveExecutionsPage>(Error.Validation(LiveErrors.Invalid, $"page starts at 1 and size is 1 to {MaxSize}"));
        }

        var page = await store.ListExecutionsAsync(query.AccountId, from, to, (query.Page - 1) * query.Size, query.Size, cancellationToken);
        var summary = await store.ExecutionSummaryAsync(to, cancellationToken);
        var evidence = LiveExecutionMapping.Evidence("entries of approved live tickets, one per ticket and account", page.Total, page.Items.Count > 0 ? page.Items.Min(i => i.Row.ForDate) : summary.First, page.Items.Count > 0 ? page.Items.Max(i => i.Row.ForDate) : summary.Last, settings.Edges);
        return Result.Success(new LiveExecutionsPage([.. page.Items.Select(LiveExecutionMapping.Execution)], query.Page, query.Size, page.Total, evidence));
    }
}
