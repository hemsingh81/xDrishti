namespace XDrishti.Domain.Options.Remark;

/// <summary>What the archive says about the held strike at one bar time. <see cref="Missing"/> is the first member: an uninitialised state is never a price.</summary>
public enum HeldState
{
    /// <summary>No usable bar carries the strike at that time: outside the offset window, or the bar is flagged suspect. Never a zero and never the last price.</summary>
    Missing,

    /// <summary>A bar carries the strike but its key-session did not pass the archive quality checks.</summary>
    Unusable,

    /// <summary>A bar carries the strike and may be read.</summary>
    Available,
}
