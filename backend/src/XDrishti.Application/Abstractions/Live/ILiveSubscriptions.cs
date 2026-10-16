using XDrishti.Application.Live.Push;

namespace XDrishti.Application.Abstractions.Live;

/// <summary>
/// What the SignalR hub of <c>xd-api</c> does with a connection (ADR 0024 §7); the hub only passes calls through. A connection belongs to one owner session. A connection that never subscribed gets the feed status and no prices.
/// </summary>
public interface ILiveSubscriptions
{
    /// <summary>A screen connected with the session <paramref name="sessionId"/>: it is sent the feed status now and every 15 seconds, and is disconnected when the session ends.</summary>
    ValueTask ConnectedAsync(string connectionId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Follows the instruments of <paramref name="scope"/> (replacing an earlier scope of the connection) and answers how many; their current prices follow within about a second.</summary>
    ValueTask<SubscribeAck> SubscribeAsync(string connectionId, LiveScope? scope, CancellationToken cancellationToken);

    /// <summary>Stops the prices of the connection (the status continues).</summary>
    void Unsubscribe(string connectionId);

    /// <summary>The connection is gone.</summary>
    void Disconnected(string connectionId);
}
