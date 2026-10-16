using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Live.Push;

namespace XDrishti.Api.Live;

/// <summary>The way out to the connected screens: sends the hub's events and closes a connection whose session ended. Remembers each connection's context only to be able to abort it.</summary>
internal sealed class SignalRLiveClients(IHubContext<EventsHub> hub) : ILiveClients
{
    private readonly ConcurrentDictionary<string, HubCallerContext> _contexts = new(StringComparer.Ordinal);

    public void Register(HubCallerContext context) => _contexts[context.ConnectionId] = context;

    public void Unregister(string connectionId) => _contexts.TryRemove(connectionId, out _);

    public async ValueTask SendPricesAsync(string connectionId, PricesChanged message, CancellationToken cancellationToken) =>
        await hub.Clients.Client(connectionId).SendAsync(LiveHubContract.PricesChangedEvent, message, cancellationToken);

    public async ValueTask SendStatusAsync(string connectionId, LiveFeedStatus message, CancellationToken cancellationToken) =>
        await hub.Clients.Client(connectionId).SendAsync(LiveHubContract.FeedStatusEvent, message, cancellationToken);

    public void Disconnect(string connectionId)
    {
        if (_contexts.TryRemove(connectionId, out var context))
        {
            context.Abort();
        }
    }
}
