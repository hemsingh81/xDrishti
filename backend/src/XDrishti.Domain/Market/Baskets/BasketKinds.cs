namespace XDrishti.Domain.Market.Baskets;

/// <summary>The kinds of basket the API lists (part of the contract): the owner's own baskets and the read-only system baskets built from portfolio buckets (ADR 0014).</summary>
public static class BasketKinds
{
    /// <summary>A basket the owner creates and maintains; stored in <c>market.baskets</c>.</summary>
    public const string Manual = "manual";

    /// <summary>A portfolio bucket shown as a read-only system basket; never stored as a basket.</summary>
    public const string Portfolio = "portfolio";
}
