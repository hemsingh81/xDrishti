namespace XDrishti.Domain.Portfolio;

/// <summary>The life of a price alert: <c>active</c> until it triggers once or the owner cancels it. Stored by name (lower case) — never rename a value.</summary>
public enum AlertStatus
{
    Active,
    Triggered,
    Cancelled,
}
