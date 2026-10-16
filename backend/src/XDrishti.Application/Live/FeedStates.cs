using XDrishti.Domain.Platform;

namespace XDrishti.Application.Live;

/// <summary>The state words of the feed's heartbeat (column <c>state</c>, at most 32 characters; the Services screen and the health check read them).</summary>
public static class FeedStates
{
    /// <summary>Outside the window: the feed is up as a process and says so, but holds no connection.</summary>
    public const string Closed = "closed";

    /// <summary>The service is paused by the owner.</summary>
    public const string Paused = "paused";

    /// <summary>In the window, but the Data account has no usable token: nothing is connected.</summary>
    public const string NoToken = "no_token";

    /// <summary>Dhan refused the token: no attempts until it changes.</summary>
    public const string TokenRefused = "token_refused";

    /// <summary>Too many reconnects: attempts are paused for a few minutes.</summary>
    public const string Flapping = "flapping";

    public const string Connecting = "connecting";

    /// <summary>Connected after a drop, subscribing again.</summary>
    public const string Reconnecting = "reconnecting";

    /// <summary>Connected with an empty active set: nothing is subscribed.</summary>
    public const string Idle = "idle";

    /// <summary>Connected and streaming the active set.</summary>
    public const string Connected = "connected";

    /// <summary>The last beat of a feed that was told to stop (a deploy, <c>xd-down</c>): a stop on purpose, not a crash (the System status page does not call the system unhealthy for it for ten minutes).</summary>
    public const string Stopped = ServiceHeartbeat.StoppedState;
}
