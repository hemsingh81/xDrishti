namespace XDrishti.Domain.Portfolio;

/// <summary>The state of an import batch. Stored by name (lower case) — never rename a value.</summary>
public enum TradeBatchStatus
{
    /// <summary>Its trades count.</summary>
    Imported,

    /// <summary>The owner undid the import: its trades are voided.</summary>
    Undone,
}
