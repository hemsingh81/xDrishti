using XDrishti.Application.Live.Push;

namespace XDrishti.Application.Abstractions.Live;

/// <summary>The way out to the connected screens (implemented by the SignalR host). A send to a connection that has gone may throw; the caller carries on with the others.</summary>
public interface ILiveClients
{
    ValueTask SendPricesAsync(string connectionId, PricesChanged message, CancellationToken cancellationToken);

    ValueTask SendStatusAsync(string connectionId, LiveFeedStatus message, CancellationToken cancellationToken);

    /// <summary>Closes the connection (its session ended).</summary>
    void Disconnect(string connectionId);
}
