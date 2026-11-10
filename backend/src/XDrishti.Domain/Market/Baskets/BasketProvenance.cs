namespace XDrishti.Domain.Market.Baskets;

/// <summary>How a membership row came to be (<c>basket_members.provenance</c>).</summary>
public static class BasketProvenance
{
    public const string Manual = "manual";
    public const string Fetched = "fetched";
    public const string Imported = "imported";
    public const string Recomputed = "recomputed";
    public const string Derived = "derived";
}
