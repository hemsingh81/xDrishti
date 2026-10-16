using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>A local WebSocket server that speaks like the feed: it records the query of the upgrade, can refuse it with a status, receives the text messages of the client and sends binary frames.</summary>
internal sealed class FeedTestServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Channel<ServerConnection> _connections = Channel.CreateUnbounded<ServerConnection>();
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly List<string> _queries = [];

    public FeedTestServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Endpoint = new Uri($"ws://127.0.0.1:{port}");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    /// <summary>The address to connect to instead of Dhan's.</summary>
    public Uri Endpoint { get; }

    /// <summary>When set, the next upgrades are refused with this status.</summary>
    public int? RejectWith { get; set; }

    /// <summary>The raw query strings of the upgrade requests so far.</summary>
    public IReadOnlyList<string> Queries
    {
        get
        {
            lock (_gate)
            {
                return [.. _queries];
            }
        }
    }

    public ValueTask<ServerConnection> NextConnectionAsync(CancellationToken cancellationToken) => _connections.Reader.ReadAsync(cancellationToken);

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            lock (_gate)
            {
                _queries.Add(context.Request.Url!.Query);
            }

            if (RejectWith is { } status)
            {
                context.Response.StatusCode = status;
                context.Response.Close();
                continue;
            }

            var accepted = await context.AcceptWebSocketAsync(null);
            var connection = new ServerConnection(accepted.WebSocket, context);
            _connections.Writer.TryWrite(connection);
            connection.StartReceiving(_stop.Token);
        }
    }
}

internal sealed class ServerConnection(WebSocket socket, HttpListenerContext context)
{
    private readonly Channel<string> _received = Channel.CreateUnbounded<string>();

    public void StartReceiving(CancellationToken cancellationToken) => _ = ReceiveLoopAsync(cancellationToken);

    /// <summary>The next text message the client sent.</summary>
    public ValueTask<string> NextMessageAsync(CancellationToken cancellationToken) => _received.Reader.ReadAsync(cancellationToken);

    public Task SendAsync(byte[] frame, CancellationToken cancellationToken) =>
        socket.SendAsync(frame, WebSocketMessageType.Binary, true, cancellationToken);

    public Task SendTextAsync(string text, CancellationToken cancellationToken) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellationToken);

    /// <summary>The server closes the connection: sends its closing frame (it does not wait for the client's answer).</summary>
    public Task CloseAsync(CancellationToken cancellationToken) => socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken);

    /// <summary>The connection is cut without a closing handshake.</summary>
    public void Abort()
    {
        socket.Abort();
        context.Response.Abort(); // closes the TCP connection under the WebSocket
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    _received.Writer.TryWrite(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // the connection ended
        }
        finally
        {
            _received.Writer.TryComplete();
        }
    }
}
