namespace XDrishti.Application.Live;

/// <summary>Why a feed session ended.</summary>
internal enum FeedSessionEnd
{
    /// <summary>It was cancelled: the window ended, the service was paused or the process is stopping.</summary>
    Stopped,

    /// <summary>Dhan refused the Data account's token; no attempts until the token changes.</summary>
    TokenRefused,

    /// <summary>More than 10 reconnects in 5 minutes: attempts must pause for 5 minutes.</summary>
    Flapping,

    /// <summary>The Data account has no usable token any more.</summary>
    NoToken,
}

/// <summary>How a session ended and the fingerprint of the credentials it last used (null when it never read them).</summary>
internal sealed record FeedSessionResult(FeedSessionEnd End, string? CredentialFingerprint = null);

/// <summary>One window's worth of connection management: connect, subscribe, stream, reconnect. The supervisor owns the window and the run record; the session owns the connection.</summary>
internal interface IFeedSession
{
    /// <summary>The state word of the session now (<see cref="FeedStates"/>).</summary>
    string State { get; }

    /// <summary>Runs until cancelled or until it cannot go on (<see cref="FeedSessionEnd"/>). Cancellation is reported as <see cref="FeedSessionEnd.Stopped"/>, not thrown.</summary>
    Task<FeedSessionResult> RunAsync(CancellationToken cancellationToken);
}
