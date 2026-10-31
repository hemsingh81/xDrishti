namespace XDrishti.Application.Abstractions.Ticks;

/// <summary>Where a day's tick set came from (the value of <c>market.tick_session_instruments.source</c>).</summary>
public static class TickSources
{
    /// <summary>A copy of the depth recorder's stored set of the day.</summary>
    public const string DepthSet = "depth_set";

    /// <summary>Built the same way as the depth set, because none was stored in time.</summary>
    public const string Built = "built";
}

/// <summary>An instrument of the day's tick set with the Dhan security id, where the set came from, why the instrument is in it and in which place.</summary>
public sealed record TickSetEntry(Guid InstrumentId, int SecurityId, string Symbol, string Source, string Reason, int Rank);

/// <summary>The row written when a day's tick recording is over (<c>market.tick_sessions</c>). <see cref="EndReason"/> takes the words of the depth recorder's end reasons.</summary>
public sealed record TickSessionRecord(DateOnly Date, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string EndReason, int Instruments, long Packets, long Rows, long Unchanged, long DroppedRows, int GapSeconds, long? StoredBytes, string? Note);

/// <summary>
/// The write side of the tick tables (P7-16b) and the measures of the disk guard. <b>There is no method that reads a tick row</b> (an architecture test keeps it so): nothing in P7 to P12 may read the recorded data. Rows only go in; nothing is
/// updated or deleted (the runtime role cannot). It reads the depth recorder's stored set of a day (read only) and <b>never writes a depth table</b>.
/// </summary>
public interface ITickStore
{
    /// <summary>Writes the rows in one transaction with <c>COPY</c>; all or none. Returns how many.</summary>
    ValueTask<int> AppendAsync(IReadOnlyList<TradeTick> rows, CancellationToken cancellationToken);

    /// <summary>The stored size of <c>market.trade_ticks</c> in bytes (<c>hypertable_detailed_size</c>).</summary>
    ValueTask<long> StoredBytesAsync(CancellationToken cancellationToken);

    /// <summary>The free bytes of the file system this process runs on; null when they cannot be read.</summary>
    ValueTask<long?> FreeBytesAsync(CancellationToken cancellationToken);

    /// <summary>The depth recorder's stored set of the day (read only, source <see cref="TickSources.DepthSet"/>); empty when there is none.</summary>
    ValueTask<IReadOnlyList<TickSetEntry>> LoadDepthSetAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>The tick set already stored for the day (fixed for the day); empty when there is none.</summary>
    ValueTask<IReadOnlyList<TickSetEntry>> LoadSetAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>Stores the day's tick set (one row per instrument; a stored row is left alone).</summary>
    ValueTask SaveSetAsync(DateOnly date, IReadOnlyList<TickSetEntry> set, CancellationToken cancellationToken);

    /// <summary>Writes the end row of a day; a day that has one keeps it (false then).</summary>
    ValueTask<bool> RecordSessionAsync(TickSessionRecord session, CancellationToken cancellationToken);

    /// <summary>The days that have a stored tick set and no end row: a process died in them.</summary>
    ValueTask<IReadOnlyList<DateOnly>> FindUnfinishedDaysAsync(CancellationToken cancellationToken);
}
