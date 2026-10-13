using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Groups;

namespace XDrishti.Application.Portfolio;

/// <summary>Reads the sectors and basket memberships the grouping needs: one read per kind for all the holdings' instruments, and nothing for a kind that is not needed.</summary>
internal static class ClassificationLoader
{
    public static async ValueTask<Classification> LoadAsync(
        IHoldingClassifications source, IEnumerable<HoldingData> holdings, bool sectors, bool baskets, CancellationToken cancellationToken)
    {
        if (!sectors && !baskets)
        {
            return Classification.None;
        }

        var ids = holdings.Where(h => h.InstrumentId is not null).Select(h => h.InstrumentId!.Value).Distinct().ToArray();
        var sectorsOf = sectors ? await source.SectorsAsync(ids, cancellationToken) : Classification.None.Sectors;
        var basketsOf = baskets ? await source.BasketsAsync(ids, cancellationToken) : Classification.None.Baskets;
        return new Classification(sectorsOf, basketsOf);
    }
}
