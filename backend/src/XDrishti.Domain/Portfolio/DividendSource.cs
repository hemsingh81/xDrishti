namespace XDrishti.Domain.Portfolio;

/// <summary>Where a dividend came from: the owner's hand (ADR 0026) or a dividend file (ADR 0028). Stored by name (lower case) — never rename a value.</summary>
public enum DividendSource
{
    Manual,
    Csv,
}
