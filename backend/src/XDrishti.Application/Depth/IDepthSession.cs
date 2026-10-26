using XDrishti.Application.Abstractions.Depth;

namespace XDrishti.Application.Depth;

/// <summary>Why a recording session ended.</summary>
internal enum DepthSessionEnd
{
    /// <summary>It was cancelled: the window ended, a gate closed or the process is stopping.</summary>
    Stopped,

    /// <summary>Dhan refused the Data account's token (401, 403, a token disconnect code): no attempts until it changes.</summary>
    TokenRefused,

    /// <summary>Dhan answered 806: the Data APIs (so depth) are not subscribed: no attempts until the credentials change.</summary>
    NotSubscribed,

    /// <summary>More than 10 reconnects in 5 minutes (or Dhan answered 805): attempts must pause for 5 minutes.</summary>
    Flapping,

    /// <summary>The Data account has no usable token any more.</summary>
    NoToken,

    /// <summary>The stored size went over its cap.</summary>
    DiskBudget,

    /// <summary>The free space went under its floor.</summary>
    DiskFree,
}

/// <summary>The counts of a session.</summary>
internal sealed record DepthRunStats(long Snapshots, long Packets, long DroppedRows, int GapSeconds, int Reconnects);

/// <summary>How a session ended, the fingerprint of the credentials it last used (null when it never read them) and what it recorded.</summary>
internal sealed record DepthSessionResult(DepthSessionEnd End, string? CredentialFingerprint, DepthRunStats Stats);

/// <summary>One run's worth of recording: connect, subscribe, sample, flush, guard the disk, reconnect. The supervisor owns the gates, the window and the run record; the session owns the connection.</summary>
internal interface IDepthSession
{
    /// <summary>The state word of the session now (<see cref="DepthStates"/>).</summary>
    string State { get; }

    /// <summary>The counts so far.</summary>
    DepthRunStats Stats { get; }

    /// <summary>Runs until cancelled or until it cannot go on. Cancellation is reported as <see cref="DepthSessionEnd.Stopped"/>, not thrown.</summary>
    Task<DepthSessionResult> RunAsync(IReadOnlyList<DepthSetEntry> set, CancellationToken cancellationToken);
}
