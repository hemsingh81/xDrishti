namespace XDrishti.Domain.Portfolio;

/// <summary>How a holdings sync ended. Stored by name (lower case) — never rename a value.</summary>
public enum SyncStatus
{
    Running,
    Succeeded,
    Failed,

    /// <summary>An automatic run that was not attempted (the account is not connected).</summary>
    Skipped,
}
