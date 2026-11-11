using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;

namespace XDrishti.Application.Selection;

internal sealed class GetBasketSettingsHandler(IBasketSettingsStore store) : IQueryHandler<GetBasketSettingsQuery, BasketSettingsView>
{
    public ValueTask<BasketSettingsView> HandleAsync(GetBasketSettingsQuery query, CancellationToken cancellationToken) => BasketSettingsViews.ReadAsync(store, cancellationToken);
}
