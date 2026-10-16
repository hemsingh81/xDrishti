using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// One open WebSocket to Dhan's market feed (ADR 0024). Reads whole binary messages (fragments are put together, up to <see cref="MaxMessageBytes"/>), hands each to the <see cref="DhanFeedPacketParser"/> and yields the packets; a message that
/// does not parse is counted (<see cref="DroppedPackets"/>) and the loop goes on. Answers nothing itself (the library answers pings). Subscriptions are JSON text messages
/// <c>{"RequestCode":15|17,"InstrumentCount":n,"InstrumentList":[{"ExchangeSegment":"NSE_EQ"|"IDX_I","SecurityId":"1333"}]}</c> - 15 subscribes in ticker mode, 17 in quote mode, 16 and 18 unsubscribe. <b>UNVERIFIED</b> (from Dhan's
/// documentation). Never logs a packet, a message or an exception text; sends are serialised (the socket allows one at a time).
/// </summary>
internal sealed class DhanFeedConnection(ClientWebSocket socket, DhanFeedPacketParser parser, FeedPacketStats stats, ILogger<DhanFeedConnection> logger) : ILiveFeedConnection
{
    /// <summary>Messages of the feed are 10 to 50 bytes; anything above this is not a packet.</summary>
    public const int MaxMessageBytes = 64 * 1024;

    public const int SubscribeTickerCode = 15;

    public const int UnsubscribeTickerCode = 16;

    public const int SubscribeQuoteCode = 17;

    public const int UnsubscribeQuoteCode = 18;

    public const string ClosedByServer = "closed by the server";

    public const string NetworkError = "network error";

    public const string Stopped = "stopped";

    private readonly SemaphoreSlim _sending = new(1, 1);
    private long _dropped;
    private volatile string _endReason = string.Empty;

    public long DroppedPackets => Interlocked.Read(ref _dropped);

    public string EndReason => _endReason;

    public ValueTask SubscribeAsync(FeedMode mode, IReadOnlyList<FeedSecurity> securities, CancellationToken cancellationToken) =>
        SendAsync(mode == FeedMode.Quote ? SubscribeQuoteCode : SubscribeTickerCode, securities, cancellationToken);

    public ValueTask UnsubscribeAsync(FeedMode mode, IReadOnlyList<FeedSecurity> securities, CancellationToken cancellationToken) =>
        SendAsync(mode == FeedMode.Quote ? UnsubscribeQuoteCode : UnsubscribeTickerCode, securities, cancellationToken);

    /// <summary>The JSON of a subscribe or unsubscribe message.</summary>
    internal static byte[] Request(int requestCode, IReadOnlyList<FeedSecurity> securities)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("RequestCode", requestCode);
            writer.WriteNumber("InstrumentCount", securities.Count);
            writer.WriteStartArray("InstrumentList");
            foreach (var security in securities)
            {
                writer.WriteStartObject();
                writer.WriteString("ExchangeSegment", security.Segment);
                writer.WriteString("SecurityId", security.SecurityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public async IAsyncEnumerable<FeedPacket> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        var oversized = false;
        long messageBytes = 0;
        while (socket.State == WebSocketState.Open)
        {
            ValueWebSocketReceiveResult received = default;
            var ended = false;
            try
            {
                received = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _endReason = Stopped;
                ended = true;
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException or InvalidOperationException)
            {
                _endReason = NetworkError;
                ended = true;
            }

            if (ended)
            {
                yield break;
            }

            if (received.MessageType == WebSocketMessageType.Close)
            {
                _endReason = ClosedByServer;
                yield break;
            }

            messageBytes += received.Count;
            if (message.Length + received.Count > MaxMessageBytes)
            {
                oversized = true;
            }
            else if (!oversized)
            {
                message.Write(buffer, 0, received.Count);
            }

            if (!received.EndOfMessage)
            {
                continue;
            }

            var isBinary = received.MessageType == WebSocketMessageType.Binary;
            var frame = (int)message.Length;
            var seen = (int)Math.Min(messageBytes, int.MaxValue);
            if (!isBinary || oversized || frame == 0)
            {
                // not a frame of packets: one drop for the whole message (only its length is kept)
                stats.Record(null, seen, null, !isBinary ? FeedDropReason.NotBinary : oversized ? FeedDropReason.Oversized : FeedDropReason.TooShort);
                Interlocked.Increment(ref _dropped);
            }

            var offset = 0;
            while (isBinary && !oversized && offset < frame)
            {
                // A frame holds one or several packets back to back; each is counted by itself. The frame's length is what the statistics keep.
                offset += Consume(message.GetBuffer(), offset, frame, out var packet);
                if (packet is not null)
                {
                    yield return packet;
                }
            }

            message.SetLength(0);
            oversized = false;
            messageBytes = 0;
        }

        if (_endReason.Length == 0)
        {
            _endReason = ClosedByServer;
        }
    }

    /// <summary>Reads the packet at <paramref name="offset"/> of the frame, counts it (code, outcome, lengths - never the bytes) and returns the bytes it used.</summary>
    private int Consume(byte[] buffer, int offset, int frame, out FeedPacket? packet)
    {
        var rest = buffer.AsSpan(offset, frame - offset);
        var used = parser.ParseNext(rest, out packet, out var reason, out var declared);
        int? code = rest.Length >= 1 ? rest[0] : null;
        stats.Record(code, frame, declared, reason, inMultiPacketFrame: offset > 0 || used < rest.Length);
        if (packet is null)
        {
            Interlocked.Increment(ref _dropped);
        }

        return used;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, wait.Token);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            // the connection is gone already; nothing more to say to it
        }
        finally
        {
            socket.Dispose();
            _sending.Dispose();
        }
    }

    private async ValueTask SendAsync(int requestCode, IReadOnlyList<FeedSecurity> securities, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(securities);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(securities.Count, ILiveFeedConnection.MaxBatch);
        if (securities.Count == 0)
        {
            return;
        }

        var payload = Request(requestCode, securities);
        await _sending.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(payload.AsMemory(), WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sending.Release();
        }

        FeedConnectionLog.Sent(logger, requestCode, securities.Count);
    }
}

/// <summary>Structured log events of the connection (event ids 4141 to 4145): the request code and a count, never the securities, the URL or a packet.</summary>
internal static partial class FeedConnectionLog
{
    [LoggerMessage(EventId = 4141, Level = LogLevel.Debug, Message = "feed.request_sent code={Code} instruments={Instruments}")]
    public static partial void Sent(ILogger logger, int code, int instruments);
}
