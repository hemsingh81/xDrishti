using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Planning;

/// <summary>The stored scope with its baskets expanded to their members now (spec AU-16). A removed or archived basket contributes no instruments.</summary>
internal sealed class AutoScopeResolver(IAutoScopeStore store, IBasketQueries baskets) : IAutoScopeSource
{
    public ValueTask<ResolvedAutoScope?> ResolveAsync(CancellationToken cancellationToken) => ResolveAsync(null, cancellationToken);

    /// <summary>
    /// The scope with its baskets expanded to their members <paramref name="asOf"/> the close of that India date (SR-01, Q8); with <c>null</c> exactly the live read above (members now). The scope itself is the stored one in force: the store keeps no
    /// older read, so a scope chosen after <paramref name="asOf"/> still applies (hindsight, stated on the replay). A system basket is clamped to its known-since date by the basket query (ISS-22).
    /// </summary>
    // shortcut: the as-of branch has no production caller since SR-01 part C (the replay reads the stored baskets per session); kept for REVIEW-2026-11-10 Q3 (plan-session members for Auto), which will call it.
    public async ValueTask<ResolvedAutoScope?> ResolveAsync(DateOnly? asOf, CancellationToken cancellationToken)
    {
        // shortcut: the store has no history reader, so on the as-of path the latest scope definition applies (hindsight, under settings_now; part B stores the scope hash); upgrade when the scopes are listable by date.
        var scope = await store.LatestAsync(cancellationToken);
        if (scope is null)
        {
            return null;
        }

        var ids = new HashSet<Guid>(scope.InstrumentIds);
        foreach (var basketId in scope.BasketIds)
        {
            if (asOf is { } day)
            {
                if (await baskets.MembersAsync(basketId, BasketMember.CutoffAfter(day), null, cancellationToken) is { } past && !(past.ArchivedAt is { } archived && archived <= BasketMember.CutoffAfter(day)))
                {
                    ids.UnionWith(past.Items.Select(m => m.Instrument.Id));
                }
            }
            else if (await baskets.MembersAsync(basketId, null, null, cancellationToken) is { ArchivedAt: null } members)
            {
                ids.UnionWith(members.Items.Where(m => m.ValidTo is null).Select(m => m.Instrument.Id));
            }
        }

        return new ResolvedAutoScope(scope.Hash, ids);
    }
}
