namespace XDrishti.Domain.Portfolio;

/// <summary>The state of a dividend import batch. Stored by name (lower case) — never rename a value.</summary>
public enum DividendBatchStatus
{
    /// <summary>Its dividends count.</summary>
    Imported,

    /// <summary>The owner undid the import: its dividends are voided.</summary>
    Undone,
}
