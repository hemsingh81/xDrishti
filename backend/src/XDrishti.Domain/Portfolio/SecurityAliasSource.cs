namespace XDrishti.Domain.Portfolio;

/// <summary>Which file taught the application that a security name belongs to an ISIN (ADR 0029). Stored by name (lower case) — never rename a value.</summary>
public enum SecurityAliasSource
{
    /// <summary>A demat holding statement.</summary>
    Demat,

    /// <summary>A depository-charges sheet.</summary>
    Dp,

    /// <summary>The owner typed the ISIN when choosing what a name of a report means.</summary>
    Choice,
}
