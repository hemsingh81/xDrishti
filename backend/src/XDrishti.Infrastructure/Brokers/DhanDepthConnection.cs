using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Depth;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// One open WebSocket to Dhan's 20-level depth feed (P7-16). Reads whole binary messages (fragments are put together, up to <see cref="MaxMessageBytes"/>), walks the packets of each with <see cref="DhanDepth20Parser"/> and yields them; a message
/// or packet that does not parse is counted (<see cref="DroppedPackets"/>) and the loop goes on. The subscription is one JSON text message <c>{"RequestCode":23,"InstrumentCount":n,"InstrumentList":[{"ExchangeSegment":"NSE_EQ","SecurityId":"1333"}]}</c>
/// for at most 50 instruments. <b>UNVERIFIED</b> (from Dhan's documentation). Never logs a packet, a message, a security or an exception text.
/// </summary>
internal sealed class DhanDepthConnection(ClientWebSocket socket, ILogger<DhanDepthConnection> logger) : IDepthFeedConnection
{
    /// <summary>A frame holds a handful of 332-byte packets; anything above this is not a frame of packets.</summary>
    public const int MaxMessageBytes = 256 * 1024;

    public const int SubscribeCode = 23;

    public const string Segment = "NSE_EQ";

    public const string ClosedByServer = "closed by the server";

    public const string NetworkError = "network error";

    public const string Stopped = "stopped";

    private long _dropped;
    private volatile string _endReason = string.Empty;

    public long DroppedPackets => Interlocked.Read(ref _dropped);

    public string EndReason => _endReason;

    /// <summary>The JSON of the subscribe message.</summary>
    internal static byte[] Request(IReadOnlyList<int> securityIds)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("RequestCode", SubscribeCode);
            writer.WriteNumber("InstrumentCount", securityIds.Count);
            writer.WriteStartArray("InstrumentList");
            foreach (var id in securityIds)
            {
                writer.WriteStartObject();
                writer.WriteString("ExchangeSegment", Segment);
                writer.WriteString("SecurityId", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public async ValueTask SubscribeAsync(IReadOnlyList<int> securityIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(securityIds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(securityIds.Count, IDepthFeedConnection.MaxInstruments);
        if (securityIds.Count == 0)
        {
            return;
        }

        await socket.SendAsync(Request(securityIds).AsMemory(), WebSocketMessageType.Text, true, cancellationToken);
        DepthConnectionLog.Sent(logger, securityIds.Count);
    }

    public async IAsyncEnumerable<DepthPacket> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        var oversized = false;
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

            var frame = (int)message.Length;
            if (received.MessageType != WebSocketMessageType.Binary || oversized || frame == 0)
            {
                Interlocked.Increment(ref _dropped);
            }
            else
            {
                var offset = 0;
                while (offset < frame)
                {
                    offset += DhanDepth20Parser.ParseNext(message.GetBuffer().AsSpan(offset, frame - offset), out var packet, out _);
                    if (packet is null)
                    {
                        Interlocked.Increment(ref _dropped);
                    }
                    else
                    {
                        yield return packet;
                    }
                }
            }

            message.SetLength(0);
            oversized = false;
        }

        if (_endReason.Length == 0)
        {
            _endReason = ClosedByServer;
        }
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
        }
    }
}

/// <summary>Structured log event of the depth connection (event id 5331): a count, never the securities, the URL or a packet.</summary>
internal static partial class DepthConnectionLog
{
    [LoggerMessage(EventId = 5331, Level = LogLevel.Debug, Message = "depth.request_sent instruments={Instruments}")]
    public static partial void Sent(ILogger logger, int instruments);
}
