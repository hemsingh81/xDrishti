using System.Reflection;

namespace XDrishti.Domain.Research;

/// <summary>Compares two trades field by field for the guard of the trade cards (P3-04d): every field but the ones costing sets after the window cut.</summary>
public static class SimTradeDifference
{
    private static readonly PropertyInfo[] Fields =
    [
        .. typeof(SimTrade).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name is not (nameof(SimTrade.Key) or nameof(SimTrade.Costs) or nameof(SimTrade.Costing))),
    ];

    /// <summary>The name of the first field, in declaration order, whose value differs (sequences compare by value); null when the trades agree on every field but <c>Costs</c> and <c>Costing</c>.</summary>
    public static string? First(SimTrade recorded, SimTrade recomputed)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(recomputed);
        foreach (var field in Fields)
        {
            if (!Equals(field.GetValue(recorded), field.GetValue(recomputed)))
            {
                return field.Name;
            }
        }

        return null;
    }
}
