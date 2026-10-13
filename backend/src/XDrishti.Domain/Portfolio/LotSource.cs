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

    /// <summary>Rebuilt from the holding's trades (ADR 0023): a buy of the tradebook, dated by it; the date is read-only. The opening lot of a rebuild is <see cref="Import"/>, flagged as using the default date.</summary>
    Trade,
}
