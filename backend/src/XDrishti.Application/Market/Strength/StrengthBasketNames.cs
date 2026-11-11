using XDrishti.Application.Market.Baskets;

namespace XDrishti.Application.Market.Strength;

/// <summary>Finds the stored basket of a pre-registered name (BK-09): the name itself, the index baskets' "NSE " prefix ("NSE Nifty 100") or the system prefix of a list or derived basket ("List: F&amp;O stocks", "Derived: Most liquid").</summary>
internal static class StrengthBasketNames
{
    public static BasketView? Find(IReadOnlyList<BasketView> all, string name) =>
        all.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(b.Name, "NSE " + name, StringComparison.OrdinalIgnoreCase)
            || b.Name.EndsWith(": " + name, StringComparison.OrdinalIgnoreCase));
}
