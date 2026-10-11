using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>The lots of a holding with the profit/loss and XIRR of each: open lots first (oldest first), then closed ones.</summary>
public sealed record GetHoldingLotsQuery(Guid HoldingId) : IQuery<Result<HoldingLots>>;

internal sealed class GetHoldingLotsHandler(IHoldingQueries queries, TimeProvider clock) : IQueryHandler<GetHoldingLotsQuery, Result<HoldingLots>>
{
    public async ValueTask<Result<HoldingLots>> HandleAsync(GetHoldingLotsQuery query, CancellationToken cancellationToken)
    {
        var holding = await queries.GetAsync(query.HoldingId, cancellationToken);
        if (holding is null)
        {
            return Result.Failure<HoldingLots>(PortfolioErrors.HoldingNotFound);
        }

        var valuationDate = IndiaTime.Today(clock.GetUtcNow());
        var lots = holding.Lots
            .OrderBy(l => l.IsOpen ? 0 : 1)
            .ThenBy(l => l.AcquiredOn)
            .ThenBy(l => l.Id)
            .Select(l => HoldingViews.Lot(holding, l, valuationDate))
            .ToList();
        return Result.Success(new HoldingLots(holding.Id, holding.TradingSymbol, holding.LastPrice, valuationDate, lots));
    }
}
