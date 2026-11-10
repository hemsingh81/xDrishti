namespace XDrishti.Domain.Market.Baskets;

/// <summary>The kinds of basket the API lists (part of the contract): the owner's own baskets, the read-only system baskets built from portfolio buckets (ADR 0014) and the stored system baskets of BK-00.</summary>
public static class BasketKinds
{
    /// <summary>A basket the owner creates and maintains; stored in <c>market.baskets</c>.</summary>
    public const string Manual = "manual";

    /// <summary>A portfolio bucket shown as a read-only system basket; never stored as a basket.</summary>
    public const string Portfolio = "portfolio";

    /// <summary>An official NSE index list (BK-01): stored, read-only, membership from the fetched files.</summary>
    public const string Index = "index";

    /// <summary>A published list (BK-05); stored, read-only.</summary>
    public const string List = "list";

    /// <summary>A basket derived from our own data (BK-02/03); stored, read-only.</summary>
    public const string Derived = "derived";
}
