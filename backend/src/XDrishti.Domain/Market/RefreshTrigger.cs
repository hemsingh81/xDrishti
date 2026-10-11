namespace XDrishti.Domain.Market;

/// <summary>Who started a master-list refresh.</summary>
public enum RefreshTrigger
{
    /// <summary>The owner pressed "Refresh list".</summary>
    Manual,

    /// <summary>The daily automatic run.</summary>
    Daily,

    /// <summary>The owner forced a refresh (the unchanged-file check is skipped).</summary>
    Forced,
}
