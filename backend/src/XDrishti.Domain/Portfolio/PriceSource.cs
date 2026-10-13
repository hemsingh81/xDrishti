namespace XDrishti.Domain.Portfolio;

/// <summary>Where a holding's last price came from (ADR 0019 §3): the broker's holdings list at the last sync, or a market quote. Stored by name (lower case) — never rename a value.</summary>
public enum PriceSource
{
    Sync,
    Quote,
}
