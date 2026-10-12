using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// The baskets with their member counts: first the system baskets (portfolio buckets, ADR 0014: account, then name; never archived), then the owner's baskets in
/// their order; archived manual ones only on request.
/// </summary>
public sealed record ListBasketsQuery(bool IncludeArchived = false) : IQuery<IReadOnlyList<BasketView>>;

internal sealed class ListBasketsHandler(IBasketQueries queries, IPortfolioBasketSource portfolio) : IQueryHandler<ListBasketsQuery, IReadOnlyList<BasketView>>
{
    public async ValueTask<IReadOnlyList<BasketView>> HandleAsync(ListBasketsQuery query, CancellationToken cancellationToken) =>
        [.. await portfolio.ListAsync(cancellationToken), .. await queries.ListAsync(query.IncludeArchived, cancellationToken)];
}
