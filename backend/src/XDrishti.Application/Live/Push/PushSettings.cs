namespace XDrishti.Application.Live.Push;

/// <summary>The timings and bounds of the hub's push (ADR 0024 §7). The defaults are the contract; tests use shorter ones.</summary>
internal sealed record PushSettings
{
    public static readonly PushSettings Default = new();

    /// <summary>The shortest time between two price pushes: notifications that arrive meanwhile are folded into the next one.</summary>
    public TimeSpan MinPushInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Prices are read at least this often while someone is subscribed, even without a notification (quote and sync prices are written without one).</summary>
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the feed's heartbeat is looked at.</summary>
    public TimeSpan StatusPollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The status goes out at least this often, changed or not.</summary>
    public TimeSpan StatusRefreshInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How often the sessions of the connections are checked.</summary>
    public TimeSpan SessionCheckInterval { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxInstrumentsPerMessage { get; init; } = LiveHubContract.MaxInstrumentsPerMessage;

    public int MaxMessageBytes { get; init; } = LiveHubContract.MaxMessageBytes;

    public int MaxInstrumentsPerConnection { get; init; } = LiveHubContract.MaxInstrumentsPerConnection;

    public int MaxHoldingIds { get; init; } = LiveHubContract.MaxHoldingIds;
}
