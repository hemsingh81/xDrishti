using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Market.Baskets;

/// <summary>Which owner-act switches are on (BK-04): <c>fetchEnabled</c> = Baskets:System:Fetch:Enabled (index lists), <c>listFetchEnabled</c> = Baskets:System:Lists:FetchEnabled (the two NSE F&amp;O files), <c>derivedEnabled</c> = Baskets:System:Derived:Enabled. The screen disables its buttons from these; the API stays the authority.</summary>
public sealed record SystemBasketSettingsView(bool FetchEnabled, bool ListFetchEnabled, bool DerivedEnabled);

/// <summary>The three system-basket switches. Read only; no setting can be changed here.</summary>
public sealed record GetSystemBasketSettingsQuery : IQuery<SystemBasketSettingsView>;

internal sealed class GetSystemBasketSettingsHandler(IBasketFetchSettings fetch, IListSettings lists, IDerivedSettings derived)
    : IQueryHandler<GetSystemBasketSettingsQuery, SystemBasketSettingsView>
{
    public ValueTask<SystemBasketSettingsView> HandleAsync(GetSystemBasketSettingsQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new SystemBasketSettingsView(fetch.Enabled, lists.FetchEnabled, derived.Enabled));
}
