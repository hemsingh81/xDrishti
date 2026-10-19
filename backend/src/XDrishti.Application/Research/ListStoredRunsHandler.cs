using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Research;

/// <summary>Lists stored runs (spec P3-10, D7); a page size outside 1 to 1,000 is refused before the store is asked.</summary>
internal sealed class ListStoredRunsHandler(IRunStore store) : IQueryHandler<ListStoredRunsQuery, Result<Page<StoredRunHeader>>>
{
    public async ValueTask<Result<Page<StoredRunHeader>>> HandleAsync(ListStoredRunsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Page.IsSizeValid
            ? await store.ListAsync(query.Filter, query.Page, cancellationToken)
            : Result.Failure<Page<StoredRunHeader>>(StoreErrors.PageInvalid($"The page size is 1 to {PageRequest.MaxSize}."));
    }
}
