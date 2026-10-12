using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Baskets;

/// <summary>The baskets (not archived) that hold an instrument now, in the owner's order; <c>instruments.not_found</c> when there is no such instrument.</summary>
public sealed record ListInstrumentBasketsQuery(Guid InstrumentId) : IQuery<Result<IReadOnlyList<InstrumentBasketView>>>;

internal sealed class ListInstrumentBasketsHandler(IBasketQueries queries) : IQueryHandler<ListInstrumentBasketsQuery, Result<IReadOnlyList<InstrumentBasketView>>>
{
    public async ValueTask<Result<IReadOnlyList<InstrumentBasketView>>> HandleAsync(ListInstrumentBasketsQuery query, CancellationToken cancellationToken) =>
        await queries.ForInstrumentAsync(query.InstrumentId, cancellationToken) is { } baskets
            ? Result.Success(baskets)
            : Result.Failure<IReadOnlyList<InstrumentBasketView>>(MarketErrors.NotFound);
}
