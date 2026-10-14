namespace XDrishti.Domain.Portfolio;

/// <summary>What the owner decided about a suggested dividend (ADR 0026). Stored by name (lower case) — never rename a value.</summary>
public enum DividendSuggestionState
{
    /// <summary>The owner accepted it: a dividend was created from it.</summary>
    Accepted,

    /// <summary>The owner dismissed it: it is not offered again.</summary>
    Dismissed,
}
