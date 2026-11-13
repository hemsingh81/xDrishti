namespace XDrishti.Application.Recording;

/// <summary>What the disk guard says.</summary>
internal enum RecorderDiskVerdict
{
    /// <summary>Record on.</summary>
    Ok,

    /// <summary>Record on, but the stored size is at 80 % of its cap or more: warn.</summary>
    Warning,

    /// <summary>The stored size is above its cap: stop (<c>disk_budget</c>).</summary>
    OverBudget,

    /// <summary>The free space is below its floor: stop (<c>disk_free</c>).</summary>
    LowFree,
}

/// <summary>The stop rule of the recorder, pure: the size cap is checked first, then the free-space floor; an unreadable free space leaves the size cap alone in force (the Services row says so). The app never deletes data.</summary>
internal static class RecorderDiskGuard
{
    /// <summary>How often the guard looks while recording.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public static RecorderDiskVerdict Check(long storedBytes, long? freeBytes, long maxStoredBytes, long minFreeBytes)
    {
        if (storedBytes > maxStoredBytes)
        {
            return RecorderDiskVerdict.OverBudget;
        }

        if (freeBytes is { } free && free < minFreeBytes)
        {
            return RecorderDiskVerdict.LowFree;
        }

        return storedBytes >= maxStoredBytes * 0.8 ? RecorderDiskVerdict.Warning : RecorderDiskVerdict.Ok;
    }
}
