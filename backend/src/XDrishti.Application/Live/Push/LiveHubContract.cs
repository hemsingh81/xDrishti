namespace XDrishti.Application.Live.Push;

/// <summary>
/// The names of the SignalR hub of <c>xd-api</c> (ADR 0024 §7) - the one place they are written. <c>scripts/gen-api.sh</c> carries them, with the message types, into the OpenAPI contract (<c>x-xdrishti-hubs</c>) and from there into the
/// frontend's <c>hubContract.generated.ts</c>.
/// </summary>
public static class LiveHubContract
{
    /// <summary>The hub's route (same origin, owner session cookie, WebSockets only).</summary>
    public const string Route = "/hubs/events";

    /// <summary>Server method <c>Subscribe(scope: LiveScope): SubscribeAck</c>.</summary>
    public const string Subscribe = "Subscribe";

    /// <summary>Server method <c>Unsubscribe(): void</c>.</summary>
    public const string Unsubscribe = "Unsubscribe";

    /// <summary>Client event <c>pricesChanged(PricesChanged)</c>.</summary>
    public const string PricesChangedEvent = "pricesChanged";

    /// <summary>Client event <c>feedStatus(LiveFeedStatus)</c>.</summary>
    public const string FeedStatusEvent = "feedStatus";

    /// <summary>Client event <c>labRunProgress(LabRunProgress)</c> (spec P5-02, D5): a Lab run entered a stage or advanced; at most four a second for a run.</summary>
    public const string LabRunProgressEvent = "labRunProgress";

    /// <summary>Client event <c>labRunFinished(LabRunFinished)</c> (spec P5-02, D5): the last event of a Lab run, with its final state.</summary>
    public const string LabRunFinishedEvent = "labRunFinished";

    /// <summary>Most instruments in one <c>pricesChanged</c>.</summary>
    public const int MaxInstrumentsPerMessage = 500;

    /// <summary>Most bytes of one <c>pricesChanged</c> payload (JSON, UTF-8).</summary>
    public const int MaxMessageBytes = 32 * 1024;

    /// <summary>Most instruments one connection follows.</summary>
    public const int MaxInstrumentsPerConnection = 1000;

    /// <summary>Most holding ids read from one <c>Subscribe</c>.</summary>
    public const int MaxHoldingIds = 1000;
}
