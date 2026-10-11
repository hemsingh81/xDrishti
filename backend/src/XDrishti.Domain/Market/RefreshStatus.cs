namespace XDrishti.Domain.Market;

public enum RefreshStatus
{
    Running,
    Succeeded,

    /// <summary>The source files had not changed since the last run: nothing was imported.</summary>
    Unchanged,
    Failed,
}
