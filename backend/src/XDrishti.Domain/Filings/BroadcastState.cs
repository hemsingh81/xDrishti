namespace XDrishti.Domain.Filings;

/// <summary>Whether a filing's exchange broadcast time is known (D3, the look-ahead guard).</summary>
public enum BroadcastState
{
    /// <summary>A valid instant with an offset, not in the future.</summary>
    Ok,

    /// <summary>No usable time. The date is never substituted.</summary>
    Unavailable,

    /// <summary>The rows of the file carry their own times (a multi-company csv, P15-04).</summary>
    RowLevelTs,
}
