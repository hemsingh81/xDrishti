using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;

namespace XDrishti.Application.Sizing;

internal sealed class GetBasketRiskBudgetsHandler(ISizingStore store) : IQueryHandler<GetBasketRiskBudgetsQuery, BasketRiskBudgetsView>
{
    public ValueTask<BasketRiskBudgetsView> HandleAsync(GetBasketRiskBudgetsQuery query, CancellationToken cancellationToken) => SizingViews.BudgetsAsync(store, cancellationToken);
}
