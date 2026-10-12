using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>The newest additions and removals of a basket (archived ones too), newest first: <c>Take</c> 1 to 1,000, default 200.</summary>
public sealed record GetBasketHistoryQuery(Guid BasketId, int? Take = null) : IQuery<Result<BasketHistory>>;

internal sealed class GetBasketHistoryHandler(IBasketQueries queries) : IQueryHandler<GetBasketHistoryQuery, Result<BasketHistory>>
{
    public async ValueTask<Result<BasketHistory>> HandleAsync(GetBasketHistoryQuery query, CancellationToken cancellationToken)
    {
        var take = query.Take ?? BasketLimits.DefaultHistoryTake;
        if (take is < 1 or > BasketLimits.MaxHistoryTake)
        {
            return Result.Failure<BasketHistory>(BasketErrors.Invalid("take", $"Use 1 to {BasketLimits.MaxHistoryTake:N0}."));
        }

        var history = await queries.HistoryAsync(query.BasketId, take, cancellationToken);
        return history is null ? Result.Failure<BasketHistory>(BasketErrors.NotFound) : Result.Success(history);
    }
}
