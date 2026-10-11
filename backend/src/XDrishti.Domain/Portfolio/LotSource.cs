namespace XDrishti.Domain.Portfolio;

/// <summary>How a lot came about. Stored by name (lower case) — never rename a value.</summary>
public enum LotSource
{
    /// <summary>Created by the owner's import, dated by the owner (or by the default date).</summary>
    Import,

    /// <summary>Found by a refresh: a new holding or a quantity increase, dated the day the refresh saw it.</summary>
    Added,

    /// <summary>Entered by hand.</summary>
    Manual,
}
