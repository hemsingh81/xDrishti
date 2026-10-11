namespace XDrishti.Domain.Portfolio;

/// <summary>Whether the account still holds the instrument. Stored by name (lower case) — never rename a value.</summary>
public enum HoldingStatus
{
    Open,
    Closed,
}
