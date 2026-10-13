namespace XDrishti.Domain.Portfolio;

/// <summary>Where a trade came from: a tradebook CSV or the owner's hand. Stored by name (lower case) — never rename a value; a later story adds <c>broker</c>.</summary>
public enum TradeSource
{
    Csv,
    Manual,
}
