namespace XDrishti.Domain.Portfolio;

/// <summary>
/// Where a trade came from: a tradebook CSV, the owner's hand, the broker's own trade history (ADR 0025) or a bill-level row of a downloaded Dhan report (ADR 0029; one row per bill, scrip and day, which the broker's execution-level trades replace).
/// Stored by name (lower case) — never rename a value.
/// </summary>
public enum TradeSource
{
    Csv,
    Manual,
    Broker,
    Bill,
}
