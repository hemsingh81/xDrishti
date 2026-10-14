namespace XDrishti.Domain.Portfolio;

/// <summary>Where a holding's last price came from (ADR 0019 §3, ADR 0024): the broker's holdings list at the last sync, a market quote, or a tick of the live feed. Stored by name (lower case) — never rename a value.</summary>
public enum PriceSource
{
    Sync,
    Quote,
    Live,
}
