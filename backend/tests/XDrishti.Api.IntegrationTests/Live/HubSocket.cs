using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Live;

/// <summary>
/// A client of the SignalR hub that speaks the JSON hub protocol over a WebSocket of the in-memory server - what a browser does with <c>skipNegotiation</c> and the WebSockets transport. Messages are separated by 0x1E; every
/// message is kept with its size in bytes (without the separator), so a test can check the bounds on the wire.
/// </summary>
internal sealed class HubSocket : IAsyncDisposable
{
    public const string Url = "ws://localhost/hubs/events";

    private const char Separator = '\u001e';

    private readonly WebSocket _socket;
    private readonly Channel<HubMessage> _received = Channel.CreateUnbounded<HubMessage>();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reader;
    private int _invocations;

    private HubSocket(WebSocket socket)
    {
        _socket = socket;
        _reader = Task.Run(ReadAsync);
    }

    /// <summary>Every message that was taken from the connection by a wait (events and completions, in order); <see cref="CloseAndDrainAsync"/> adds the ones nobody asked for.</summary>
    public List<HubMessage> Seen { get; } = [];

    /// <summary>Opens the WebSocket (the server answers 101 or the connect fails) and does the hub handshake.</summary>
    public static async Task<HubSocket> ConnectAsync(ApiFactory factory, string? cookieHeader, string? origin, CancellationToken cancellationToken)
    {
        var client = factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            if (cookieHeader is not null)
            {
                request.Headers["Cookie"] = cookieHeader;
            }

            if (origin is not null)
            {
                request.Headers["Origin"] = origin;
            }
        };
        var socket = await client.ConnectAsync(new Uri(Url), cancellationToken);
        var hub = new HubSocket(socket);
        await hub.SendAsync("""{"protocol":"json","version":1}""", cancellationToken);
        var handshake = await hub.NextAsync(_ => true, cancellationToken);
        handshake.Json.GetRawText().ShouldBe("{}");
        return hub;
    }

    /// <summary>Connects as the signed-in owner.</summary>
    public static Task<HubSocket> ConnectAsync(ApiFactory factory, SignedInClient owner, CancellationToken cancellationToken) =>
        ConnectAsync(factory, owner.CookieHeader, null, cancellationToken);

    /// <summary>Calls a server method and waits for its answer (events that arrive meanwhile are kept for the next wait).</summary>
    public async Task<JsonElement> InvokeAsync(string target, object?[] arguments, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _invocations).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await SendAsync(JsonSerializer.Serialize(new { type = 1, invocationId = id, target, arguments }), cancellationToken);
        var completion = await NextAsync(m => m.Type == 3 && m.Json.TryGetProperty("invocationId", out var i) && i.GetString() == id, cancellationToken);
        if (completion.Json.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException("The hub answered an error: " + error.GetString());
        }

        return completion.Json.TryGetProperty("result", out var result) ? result.Clone() : default;
    }

    /// <summary>The next event of this name; other messages are skipped.</summary>
    public Task<HubMessage> NextEventAsync(string target, CancellationToken cancellationToken) =>
        NextAsync(m => m.Type == 1 && m.Target == target, cancellationToken);

    /// <summary>Waits until the connection is closed by the server (or the socket fails); true when it was.</summary>
    public async Task<bool> WaitForCloseAsync(CancellationToken cancellationToken)
    {
        while (await _received.Reader.WaitToReadAsync(cancellationToken))
        {
            _received.Reader.TryRead(out _);
        }

        return true;
    }

    /// <summary>Closes the connection and keeps what had arrived but nobody waited for in <see cref="Seen"/>.</summary>
    public async Task CloseAndDrainAsync(CancellationToken cancellationToken)
    {
        await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
        while (await _received.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_received.Reader.TryRead(out var message))
            {
                Seen.Add(message);
            }
        }
    }

    public async Task<HubMessage> NextAsync(Func<HubMessage, bool> wanted, CancellationToken cancellationToken)
    {
        while (await _received.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_received.Reader.TryRead(out var message))
            {
                Seen.Add(message);
                if (wanted(message))
                {
                    return message;
                }
            }
        }

        throw new InvalidOperationException("The hub connection was closed.");
    }

    /// <summary>Every event of this name that arrives until <paramref name="until"/> says the collected ones are enough.</summary>
    public async Task<List<HubMessage>> CollectEventsAsync(string target, Func<List<HubMessage>, bool> until, CancellationToken cancellationToken)
    {
        var events = new List<HubMessage>();
        while (!until(events))
        {
            events.Add(await NextEventAsync(target, cancellationToken));
        }

        return events;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // already gone
            }
        }

        try
        {
            await _reader;
        }
        catch (OperationCanceledException)
        {
            // stopped
        }

        _socket.Dispose();
        _stop.Dispose();
    }

    private async Task SendAsync(string json, CancellationToken cancellationToken) =>
        await _socket.SendAsync(Encoding.UTF8.GetBytes(json + Separator), WebSocketMessageType.Text, true, cancellationToken);

    private async Task ReadAsync()
    {
        var buffer = new byte[16 * 1024];
        var pending = new List<byte>();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(buffer, _stop.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                for (var i = 0; i < result.Count; i++)
                {
                    if (buffer[i] == (byte)Separator)
                    {
                        _received.Writer.TryWrite(HubMessage.Parse([.. pending]));
                        pending.Clear();
                    }
                    else
                    {
                        pending.Add(buffer[i]);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // closed by the server or stopped by the test
        }
        finally
        {
            _received.Writer.TryComplete();
        }
    }
}

/// <summary>One hub protocol message: type 1 is an invocation (an event of the server), 3 a completion, 6 a ping, 7 a close.</summary>
internal sealed record HubMessage(int Type, string? Target, JsonElement Json, int Bytes)
{
    /// <summary>The first (and only) argument of an event.</summary>
    public JsonElement Argument => Json.GetProperty("arguments")[0];

    public static HubMessage Parse(byte[] raw)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement.Clone();
        var type = root.TryGetProperty("type", out var t) ? t.GetInt32() : 0;
        var target = root.TryGetProperty("target", out var g) ? g.GetString() : null;
        return new HubMessage(type, target, root, raw.Length);
    }
}
