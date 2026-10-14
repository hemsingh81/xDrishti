namespace XDrishti.Domain.Portfolio;

/// <summary>Where a trade came from: a tradebook CSV, the owner's hand or the broker's own trade history (ADR 0025). Stored by name (lower case) — never rename a value.</summary>
public enum TradeSource
{
    Csv,
    Manual,
    Broker,
}
