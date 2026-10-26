namespace XDrishti.Application.Depth;

/// <summary>The state words of the depth recorder's heartbeat (column <c>state</c>, at most 32 characters).</summary>
public static class DepthStates
{
    /// <summary>The deployment flag <c>Depth:Enabled</c> is false: nothing is read or opened.</summary>
    public const string Disabled = "disabled";

    /// <summary>The owner's stored switch is OFF.</summary>
    public const string SwitchOff = "switch_off";

    /// <summary>The service is paused on the Services screen.</summary>
    public const string Paused = "paused";

    /// <summary>All gates are open but it is outside the window.</summary>
    public const string Closed = "closed";

    public const string NoToken = "no_token";

    public const string TokenRefused = "token_refused";

    public const string NotSubscribed = "not_subscribed";

    public const string Flapping = "flapping";

    public const string DiskBudget = "disk_budget";

    public const string DiskFree = "disk_free";

    public const string NoInstruments = "no_instruments";

    public const string Connecting = "connecting";

    public const string Reconnecting = "reconnecting";

    public const string Recording = "recording";

    /// <summary>The last beat of a recorder that was told to stop (a deploy).</summary>
    public const string Stopped = "stopped";
}
