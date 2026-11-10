using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Application.Planning;

/// <summary>The stored scope with its baskets expanded to their members now (spec AU-16). A removed or archived basket contributes no instruments.</summary>
internal sealed class AutoScopeResolver(IAutoScopeStore store, IBasketQueries baskets) : IAutoScopeSource
{
    public async ValueTask<ResolvedAutoScope?> ResolveAsync(CancellationToken cancellationToken)
    {
        var scope = await store.LatestAsync(cancellationToken);
        if (scope is null)
        {
            return null;
        }

        var ids = new HashSet<Guid>(scope.InstrumentIds);
        foreach (var basketId in scope.BasketIds)
        {
            if (await baskets.MembersAsync(basketId, null, null, cancellationToken) is { ArchivedAt: null } members)
            {
                ids.UnionWith(members.Items.Where(m => m.ValidTo is null).Select(m => m.Instrument.Id));
            }
        }

        return new ResolvedAutoScope(scope.Hash, ids);
    }
}
