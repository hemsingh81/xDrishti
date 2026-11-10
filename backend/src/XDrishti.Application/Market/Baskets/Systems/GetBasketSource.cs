using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>The source of a system basket: its file, state and the last 20 fetches. <c>baskets.not_found</c> for a basket without a source (a manual basket, a portfolio bucket).</summary>
public sealed record GetBasketSourceQuery(Guid BasketId) : IQuery<Result<BasketSourceView>>;

internal sealed class GetBasketSourceHandler(IBasketSourceStore sources) : IQueryHandler<GetBasketSourceQuery, Result<BasketSourceView>>
{
    public const int Recent = 20;

    public async ValueTask<Result<BasketSourceView>> HandleAsync(GetBasketSourceQuery query, CancellationToken cancellationToken)
    {
        var found = await sources.ListAsync([query.BasketId], cancellationToken);
        if (found.Count == 0)
        {
            return Result.Failure<BasketSourceView>(BasketErrors.NotFound);
        }

        var s = found[0];
        var fetches = await sources.RecentFetchesAsync(s.BasketId, Recent, cancellationToken);
        return Result.Success(new BasketSourceView(
            s.BasketId, s.Name, s.FileName, s.SourceUrl, s.Enabled, s.State, s.LastOkAt, s.LastCheckedAt, s.NextAttemptAt, s.ExpectedMin, s.ExpectedMax, fetches));
    }
}
