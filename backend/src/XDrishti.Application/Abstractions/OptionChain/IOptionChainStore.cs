namespace XDrishti.Application.Abstractions.OptionChain;

/// <summary>The flag bits of a stored row (<c>options.chain_snapshots.flags</c>, 0 to 31).</summary>
public static class ChainFlags
{
    /// <summary>The bid price was zero or missing (bid price and quantity are null).</summary>
    public const short NoBid = 1;

    /// <summary>The ask price was zero or missing.</summary>
    public const short NoAsk = 2;

    /// <summary>The last price was zero or missing.</summary>
    public const short NoTrade = 4;

    /// <summary>The IV or a Greek was not a usable number (null).</summary>
    public const short BadGreeks = 8;

    /// <summary>The bid was above the ask (both kept as sent).</summary>
    public const short Crossed = 16;
}

/// <summary>One strike and side of a chain, as stored. Prices are in paise; a value Dhan did not send, or sent as zero for a missing quote, is null with a flag - never a zero.</summary>
public sealed record ChainSnapshotRow(
    DateTimeOffset Ts,
    Guid UnderlyingId,
    DateOnly Expiry,
    int StrikePx,
    string Side,
    DateTimeOffset RxAt,
    long SecurityId,
    int UnderlyingPx,
    int? LastPx,
    int? AvgPx,
    int? PrevClosePx,
    int? BidPx,
    int? BidQty,
    int? AskPx,
    int? AskQty,
    long? Oi,
    long? PrevOi,
    long? Volume,
    long? PrevVolume,
    float? Iv,
    float? Delta,
    float? Gamma,
    float? Theta,
    float? Vega,
    short Flags);

/// <summary>The row of one planned request (<c>options.chain_requests</c>): the slot, what was asked, how it ended.</summary>
public sealed record ChainRequestRecord(DateTimeOffset Slot, string Kind, Guid UnderlyingId, DateOnly? Expiry, string Outcome, int? HttpStatus, string? DhanCode, int Rows, int LatencyMs, DateTimeOffset At);

/// <summary>The row written when a day is over (<c>options.chain_sessions</c>).</summary>
public sealed record ChainSessionRecord(DateOnly Date, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string EndReason, int Slots, int Requests, int Ok, int Gaps, long Rows, long? StoredBytes, string? Note);

/// <summary>
/// The write side of the option-chain tables (P14-02) and the measures of the disk guard. <b>There is no method that reads a snapshot row</b> (an architecture test keeps it so): nothing reads the recorded data yet. Rows only go in; nothing is updated
/// or deleted (the runtime role cannot). A snapshot is inserted with <c>ON CONFLICT DO NOTHING</c>, so a re-run of a slot inserts nothing.
/// </summary>
public interface IOptionChainStore
{
    /// <summary>Writes the request row and the snapshot rows in one transaction; returns how many snapshot rows were new.</summary>
    ValueTask<int> AppendAsync(ChainRequestRecord request, IReadOnlyList<ChainSnapshotRow> rows, CancellationToken cancellationToken);

    /// <summary>The stored size of <c>options.chain_snapshots</c> in bytes (<c>hypertable_detailed_size</c>).</summary>
    ValueTask<long> StoredBytesAsync(CancellationToken cancellationToken);

    /// <summary>The free bytes of the file system this process runs on; null when they cannot be read.</summary>
    ValueTask<long?> FreeBytesAsync(CancellationToken cancellationToken);

    /// <summary>Writes the end row of a day; a day that has one keeps it (false then).</summary>
    ValueTask<bool> RecordSessionAsync(ChainSessionRecord session, CancellationToken cancellationToken);

    /// <summary>The days that have a request row and no end row: a process died in them.</summary>
    ValueTask<IReadOnlyList<DateOnly>> FindUnfinishedDaysAsync(CancellationToken cancellationToken);
}
