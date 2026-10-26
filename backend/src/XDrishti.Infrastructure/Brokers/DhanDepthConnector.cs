using System.Net;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Opens the WebSocket to Dhan's 20-level depth feed (P7-16, ADR 0133): <c>wss://depth-api-feed.dhan.co/twentydepth?token=…&amp;clientId=…&amp;authType=2</c> (no <c>version</c>) with the primary Data account's credentials. The URL is checked against the
/// allow-list (<see cref="BrokerEndpoints.IsAllowedDepthSocket"/>) <b>before any connection is made</b>. A 401 or 403 on the upgrade is <see cref="FeedConnectFailureKind.TokenRefused"/>, not an exception; any other failure is
/// <see cref="FeedConnectFailureKind.Failed"/> with a fixed message. The token, the client id and the URL are never logged and never put into a message or an exception. <b>Not verified against the real feed.</b>
/// <para><paramref name="connectTo"/> exists for the tests only: a local test server replaces the endpoint's authority in the actual connection while the allow-list check still runs on the real Dhan URL.</para>
/// </summary>
internal sealed class DhanDepthConnector(ILoggerFactory loggers, Uri? connectTo = null) : IDepthFeedConnector
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    public const string TokenRefused = "The Data account's token was refused by Dhan.";

    public const string Unreachable = "Dhan's depth feed could not be reached.";

    public const string TimedOut = "Dhan's depth feed did not answer in time.";

    public async ValueTask<DepthConnectResult> ConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var uri = BrokerEndpoints.BuildDepthUri(credentials.ClientId, credentials.AccessToken.Reveal());
        if (!BrokerEndpoints.IsAllowedDepthSocket(uri))
        {
            throw BrokerEndpointNotAllowedException.ForSocket(uri);
        }

        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = DhanFeedConnector.KeepAliveInterval;
        socket.Options.KeepAliveTimeout = DhanFeedConnector.KeepAliveTimeout;
        socket.Options.CollectHttpResponseDetails = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            await socket.ConnectAsync(Target(uri), timeout.Token);
            return DepthConnectResult.Connected(new DhanDepthConnection(socket, loggers.CreateLogger<DhanDepthConnection>()));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            return DepthConnectResult.Failed(FeedConnectFailureKind.Failed, TimedOut);
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException)
        {
            // The exception text is not used: it can name the endpoint. The upgrade's status says enough.
            var status = socket.HttpStatusCode;
            socket.Dispose();
            return status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? DepthConnectResult.Failed(FeedConnectFailureKind.TokenRefused, TokenRefused)
                : DepthConnectResult.Failed(FeedConnectFailureKind.Failed, Unreachable);
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            throw;
        }
    }

    private Uri Target(Uri allowed) => connectTo is null ? allowed : new UriBuilder(connectTo) { Path = allowed.AbsolutePath, Query = allowed.Query.TrimStart('?') }.Uri;
}
