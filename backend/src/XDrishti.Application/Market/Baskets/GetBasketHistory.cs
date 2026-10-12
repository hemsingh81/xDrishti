using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// The newest additions and removals of a basket (archived ones too), newest first: <c>Take</c> 1 to 1,000, default 200. A system (portfolio) basket has none:
/// <c>baskets.invalid</c>.
/// </summary>
public sealed record GetBasketHistoryQuery(Guid BasketId, int? Take = null) : IQuery<Result<BasketHistory>>;

internal sealed class GetBasketHistoryHandler(IBasketQueries queries, IPortfolioBasketSource portfolio) : IQueryHandler<GetBasketHistoryQuery, Result<BasketHistory>>
{
    public async ValueTask<Result<BasketHistory>> HandleAsync(GetBasketHistoryQuery query, CancellationToken cancellationToken)
    {
        var take = query.Take ?? BasketLimits.DefaultHistoryTake;
        if (take is < 1 or > BasketLimits.MaxHistoryTake)
        {
            return Result.Failure<BasketHistory>(BasketErrors.Invalid("take", $"Use 1 to {BasketLimits.MaxHistoryTake:N0}."));
        }

        var history = await queries.HistoryAsync(query.BasketId, take, cancellationToken);
        if (history is not null)
        {
            return Result.Success(history);
        }

        return Result.Failure<BasketHistory>(await portfolio.ExistsAsync(query.BasketId, cancellationToken)
            ? BasketErrors.Invalid("basketId", "A portfolio basket has no basket history: its holdings are the portfolio's.")
            : BasketErrors.NotFound);
    }
}
