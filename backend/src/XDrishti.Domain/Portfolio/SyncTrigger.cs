namespace XDrishti.Domain.Portfolio;

/// <summary>Who started a holdings sync. Stored by name (lower case) — never rename a value.</summary>
public enum SyncTrigger
{
    /// <summary>The owner pressed "Sync now".</summary>
    Manual,

    /// <summary>The automatic refresh of the account.</summary>
    Auto,

    /// <summary>The owner's import of holdings.</summary>
    Import,
}
