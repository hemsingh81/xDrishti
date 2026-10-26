namespace XDrishti.Application.Abstractions.Depth;

/// <summary>Why an instrument is in a day's recording set.</summary>
public static class DepthReasons
{
    public const string Armed = "armed";

    public const string Pinned = "pinned";

    public const string Turnover = "turnover";
}

/// <summary>An instrument of the day's set with the Dhan security id and why and in which place it was chosen.</summary>
public sealed record DepthSetEntry(Guid InstrumentId, int SecurityId, string Symbol, string Reason, int Rank);

/// <summary>How a day's recording ended (the value of <c>market.depth_sessions.end_reason</c>).</summary>
public static class DepthEndReasons
{
    public const string WindowEnd = "window_end";

    public const string Paused = "paused";

    public const string DiskBudget = "disk_budget";

    public const string DiskFree = "disk_free";

    public const string TokenRefused = "token_refused";

    public const string NotSubscribed = "not_subscribed";

    public const string Flapping = "flapping";

    public const string Crashed = "crashed";
}

/// <summary>The row written when a day's recording ends (<c>market.depth_sessions</c>).</summary>
public sealed record DepthSessionRecord(DateOnly Date, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string EndReason, int Instruments, long Snapshots, long Packets, long DroppedRows, int GapSeconds, long? StoredBytes, string? Note);

/// <summary>
/// The write side of the depth tables (P7-16) and the two measures of the disk guard. <b>There is no method that reads depth rows</b> (an architecture test keeps it so): nothing in P7 to P12 may read the recorded data. Rows only go in; nothing is
/// updated or deleted (the runtime role cannot).
/// </summary>
public interface IDepthStore
{
    /// <summary>Writes the rows in one transaction with <c>COPY</c>; all or none. Returns how many.</summary>
    ValueTask<int> AppendAsync(IReadOnlyList<DepthSnapshot> rows, CancellationToken cancellationToken);

    /// <summary>The stored size of <c>market.depth_snapshots</c> in bytes (<c>hypertable_detailed_size</c>).</summary>
    ValueTask<long> StoredBytesAsync(CancellationToken cancellationToken);

    /// <summary>The free bytes of the file system this process runs on; null when they cannot be read.</summary>
    ValueTask<long?> FreeBytesAsync(CancellationToken cancellationToken);

    /// <summary>The set already stored for the day (fixed for the day); empty when there is none.</summary>
    ValueTask<IReadOnlyList<DepthSetEntry>> LoadSetAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>Stores the day's set (one row per instrument; a stored row is left alone).</summary>
    ValueTask SaveSetAsync(DateOnly date, IReadOnlyList<DepthSetEntry> set, CancellationToken cancellationToken);

    /// <summary>Writes the end row of a day; a day that has one keeps it (false then).</summary>
    ValueTask<bool> RecordSessionAsync(DepthSessionRecord session, CancellationToken cancellationToken);

    /// <summary>The days that have a stored set and no end row: a process died in them.</summary>
    ValueTask<IReadOnlyList<DateOnly>> FindUnfinishedDaysAsync(CancellationToken cancellationToken);
}
