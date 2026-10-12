using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Market.Baskets;

/// <summary>The baskets in the owner's order with their member counts; archived ones only on request.</summary>
public sealed record ListBasketsQuery(bool IncludeArchived = false) : IQuery<IReadOnlyList<BasketView>>;

internal sealed class ListBasketsHandler(IBasketQueries queries) : IQueryHandler<ListBasketsQuery, IReadOnlyList<BasketView>>
{
    public async ValueTask<IReadOnlyList<BasketView>> HandleAsync(ListBasketsQuery query, CancellationToken cancellationToken) =>
        await queries.ListAsync(query.IncludeArchived, cancellationToken);
}
