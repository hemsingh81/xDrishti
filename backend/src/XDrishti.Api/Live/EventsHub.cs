using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Live.Push;

namespace XDrishti.Api.Live;

/// <summary>
/// The SignalR hub <c>/hubs/events</c> (ADR 0024 §7): the owner's session cookie only (every method needs a signed-in session; there is no anonymous one), same origin only (<see cref="HubOriginMiddleware"/>), WebSockets only. Thin: it passes the calls to
/// <see cref="ILiveSubscriptions"/>; the pushes are sent by the push loop through <see cref="SignalRLiveClients"/>, not from here. The server's events are <c>pricesChanged</c> and <c>feedStatus</c> (<see cref="LiveHubContract"/>).
/// </summary>
[Authorize]
internal sealed class EventsHub(ILiveSubscriptions subscriptions, SignalRLiveClients clients) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var session = Context.User?.FindFirst(SessionAuthentication.SessionIdClaim)?.Value;
        if (!Guid.TryParse(session, out var sessionId))
        {
            Context.Abort(); // cannot happen with the session scheme; a connection without a session is never kept
            return;
        }

        clients.Register(Context);
        await subscriptions.ConnectedAsync(Context.ConnectionId, sessionId, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.Disconnected(Context.ConnectionId);
        clients.Unregister(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Follow the prices of <paramref name="scope"/> (replaces an earlier scope of this connection).</summary>
    public ValueTask<SubscribeAck> Subscribe(LiveScope? scope) => subscriptions.SubscribeAsync(Context.ConnectionId, scope, Context.ConnectionAborted);

    /// <summary>Stop the prices of this connection.</summary>
    public void Unsubscribe() => subscriptions.Unsubscribe(Context.ConnectionId);
}
