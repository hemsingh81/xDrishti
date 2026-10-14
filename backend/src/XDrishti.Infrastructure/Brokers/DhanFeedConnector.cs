using System.Net;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Opens the WebSocket to Dhan's market feed (ADR 0024 §8): <c>wss://api-feed.dhan.co?version=2&amp;token=…&amp;clientId=…&amp;authType=2</c> with the primary Data account's credentials. The URL is checked against the allow-list
/// (<see cref="BrokerEndpoints.IsAllowedWebSocket"/>) <b>before any connection is made</b>. The upgrade answers 401 or 403 when the token is refused: that is <see cref="FeedConnectFailureKind.TokenRefused"/>, not an exception; any other
/// failure (network, timeout, another status) is <see cref="FeedConnectFailureKind.Failed"/> with a fixed message. The token, the client id and the URL are never logged and never put into a message or an exception; only the HTTP status and
/// the kind of failure are. The library answers the server's pings; the keep-alive (a ping every 10 s, a connection with no pong for 30 s is closed) is its own. <b>Not verified against the real feed</b> (the first real session is the owner's check).
/// <para>
/// <paramref name="connectTo"/> exists for the tests only: a local test server (scheme, host and port) replaces the endpoint's authority in the actual connection while the allow-list check still runs on the real Dhan URL.
/// </para>
/// </summary>
internal sealed class DhanFeedConnector(TimeProvider clock, ILoggerFactory loggers, Uri? connectTo = null, FeedPacketStats? packetStats = null) : ILiveFeedConnector
{
    /// <summary>The time allowed for the upgrade.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(30);

    public const string TokenRefused = "The Data account's token was refused by Dhan.";

    public const string Unreachable = "Dhan's feed could not be reached.";

    public const string TimedOut = "Dhan's feed did not answer in time.";

    private readonly DhanFeedPacketParser _parser = new(clock);

    public async ValueTask<FeedConnectResult> ConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var uri = BrokerEndpoints.BuildFeedUri(credentials.ClientId, credentials.AccessToken.Reveal());
        if (!BrokerEndpoints.IsAllowedWebSocket(uri))
        {
            throw BrokerEndpointNotAllowedException.ForSocket(uri);
        }

        var logger = loggers.CreateLogger<DhanFeedConnection>();
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = KeepAliveInterval;
        socket.Options.KeepAliveTimeout = KeepAliveTimeout;
        socket.Options.CollectHttpResponseDetails = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            await socket.ConnectAsync(Target(uri), timeout.Token);
            return FeedConnectResult.Connected(new DhanFeedConnection(socket, _parser, packetStats ?? new FeedPacketStats(), logger));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            return FeedConnectResult.Failed(FeedConnectFailureKind.Failed, TimedOut);
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException)
        {
            // The exception text is not used: it can name the endpoint. The upgrade's status says enough.
            var status = socket.HttpStatusCode;
            socket.Dispose();
            return status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? FeedConnectResult.Failed(FeedConnectFailureKind.TokenRefused, TokenRefused)
                : FeedConnectResult.Failed(FeedConnectFailureKind.Failed, Unreachable);
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            throw;
        }
    }

    private Uri Target(Uri allowed) => connectTo is null ? allowed : new UriBuilder(connectTo) { Query = allowed.Query.TrimStart('?') }.Uri;
}
