using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars;

/// <summary>The most recent data jobs, newest first (1 to 100, default 20).</summary>
public sealed record ListDataJobsQuery(int? Take) : IQuery<IReadOnlyList<DataJobView>>;

internal sealed class ListDataJobsHandler(IDataJobStore jobs) : IQueryHandler<ListDataJobsQuery, IReadOnlyList<DataJobView>>
{
    public async ValueTask<IReadOnlyList<DataJobView>> HandleAsync(ListDataJobsQuery query, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take ?? DataLimits.DefaultHistory, 1, DataLimits.MaxHistory);
        return [.. (await jobs.ListAsync(take, cancellationToken)).Select(DataJobView.From)];
    }
}

/// <summary>One data job; <c>data.job_not_found</c> when it does not exist.</summary>
public sealed record GetDataJobQuery(Guid Id) : IQuery<Result<DataJobView>>;

internal sealed class GetDataJobHandler(IDataJobStore jobs) : IQueryHandler<GetDataJobQuery, Result<DataJobView>>
{
    public async ValueTask<Result<DataJobView>> HandleAsync(GetDataJobQuery query, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(query.Id, cancellationToken);
        return job is null ? Result.Failure<DataJobView>(DataErrors.JobNotFound) : Result.Success(DataJobView.From(job));
    }
}
