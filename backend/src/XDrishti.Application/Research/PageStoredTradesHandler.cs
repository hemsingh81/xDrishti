using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>Pages the trades of a stored run (spec P3-10, D7). A page size outside 1 to 1,000 and a key that is not 64 lower-case hex are refused before the store is asked.</summary>
internal sealed class PageStoredTradesHandler(IRunStore store) : IQueryHandler<PageStoredTradesQuery, Result<Page<RunTrade>>>
{
    public async ValueTask<Result<Page<RunTrade>>> HandleAsync(PageStoredTradesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!query.Page.IsSizeValid)
        {
            return Result.Failure<Page<RunTrade>>(StoreErrors.PageInvalid($"The page size is 1 to {PageRequest.MaxSize}."));
        }

        if (!RunRecordText.IsHex64(query.RunKey))
        {
            return Result.Failure<Page<RunTrade>>(Error.NotFound(GetStoredRunHandler.NotFoundCode, "No run with this key is stored."));
        }

        return await store.PageTradesAsync(query.RunKey, query.Filter, query.Page, cancellationToken);
    }
}
