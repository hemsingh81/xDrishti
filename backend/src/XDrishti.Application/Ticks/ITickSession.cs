using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Depth;

namespace XDrishti.Application.Ticks;

/// <summary>The counts of a tick session: packets read, rows queued, quotes with an unchanged volume, rows dropped, seconds without a connection, reconnects.</summary>
internal sealed record TickRunStats(long Packets, long Rows, long Unchanged, long DroppedRows, int GapSeconds, int Reconnects);

/// <summary>How a tick session ended (the depth recorder's end words: the same causes), the fingerprint of the credentials it last used (null when it never read them) and what it recorded.</summary>
internal sealed record TickSessionResult(DepthSessionEnd End, string? CredentialFingerprint, TickRunStats Stats);

/// <summary>One run's worth of tick recording: connect, subscribe, sample, flush, guard the disk, reconnect. The supervisor owns the gates, the window and the run record; the session owns the connection.</summary>
internal interface ITickSession
{
    /// <summary>The state word of the session now (<see cref="DepthStates"/>).</summary>
    string State { get; }

    /// <summary>The counts so far.</summary>
    TickRunStats Stats { get; }

    /// <summary>Runs until cancelled or until it cannot go on. Cancellation is reported as <see cref="DepthSessionEnd.Stopped"/>, not thrown.</summary>
    Task<TickSessionResult> RunAsync(IReadOnlyList<TickSetEntry> set, CancellationToken cancellationToken);
}
