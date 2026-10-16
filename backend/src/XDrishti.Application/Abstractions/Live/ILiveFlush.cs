namespace XDrishti.Application.Abstractions.Live;

/// <summary>What one flush did. <see cref="Failed"/> means the batch was kept for the next flush and nothing is known to have been written.</summary>
public sealed record LiveFlushResult(int Ticks, int HoldingsUpdated, int QuotesWritten, int AlertsTriggered, bool Notified, bool Failed)
{
    /// <summary>A flush with nothing to write.</summary>
    public static LiveFlushResult Nothing(bool notified = false) => new(0, 0, 0, 0, notified, false);
}

/// <summary>
/// The feed's price sink (ADR 0024): ticks are added as they arrive (cheap, no I/O) and written in one batch per flush - the latest price of each instrument into the open holdings and <c>market.live_quotes</c>, then the price alerts of
/// the flushed instruments. Safe to call from the receive loop and the flush loop at the same time.
/// </summary>
public interface ILiveFlush
{
    /// <summary>Takes a tick. A tick that is not newer than the latest one held for its instrument, or has no positive price, is dropped.</summary>
    void Add(LivePrice tick);

    /// <summary>Starts a window with a clean slate: pending ticks and the memory of the newest tick times are forgotten.</summary>
    void Reset();

    /// <summary>Writes what changed since the last flush; does nothing (no statement) when nothing did.</summary>
    ValueTask<LiveFlushResult> FlushAsync(CancellationToken cancellationToken);
}
