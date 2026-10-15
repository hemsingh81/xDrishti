namespace XDrishti.Domain.Portfolio;

/// <summary>The state of a cost (depository charges) import batch. Stored by name (lower case) — never rename a value.</summary>
public enum CostBatchStatus
{
    /// <summary>Its cost entries count.</summary>
    Imported,

    /// <summary>The owner undid the import: its cost entries are voided.</summary>
    Undone,
}
