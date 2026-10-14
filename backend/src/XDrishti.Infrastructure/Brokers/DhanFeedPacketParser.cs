using System.Buffers.Binary;
using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Reads the binary messages of Dhan's market feed (ADR 0024 §2; P2-10 criterion 3). Little-endian. A message is one packet: an 8-byte header - response code (1 byte), message length (int16), exchange segment (1 byte), security id
/// (int32) - and a payload by code:
/// <list type="table">
/// <item><term>2 ticker (16 bytes)</term><description>last price float32, last trade time int32 (epoch seconds)</description></item>
/// <item><term>4 quote (50 bytes)</term><description>last price float32, last quantity int16, last trade time int32, average price float32, volume int32, total sell quantity int32, total buy quantity int32, day open, close, high, low float32</description></item>
/// <item><term>6 previous close (16 bytes)</term><description>previous close float32, previous open interest int32</description></item>
/// <item><term>50 disconnect (10 bytes)</term><description>reason code int16</description></item>
/// </list>
/// Segment codes: 0 index (<c>IDX_I</c>), 1 NSE equity (<c>NSE_EQ</c>); other segments are not carried by this application. The length field is the length of the packet including the header and a frame can hold several packets (<see cref="ParseNext"/>) - both OBSERVED on 2026-10-14 for codes 2, 4 and 6 (lengths 16, 50, 16).
/// Still <b>UNVERIFIED</b>: the disconnect packet, the segment codes, the meaning of the fields and the trade time's zone are written from Dhan's documentation (docs/research/dhan-samples/README.md); the owner's first real session confirms or corrects them. Anything
/// that does not fit - an unknown code, a length that does not match the message or the code, an unknown segment, a price that is not a positive finite number, a trade time more than a day from the clock - is dropped with a reason and counted by
/// the caller; one bad packet never ends the connection. A quote's open, high, low and close and the volume that are zero or absurd are left out (null), not a reason to drop the packet.
/// </summary>
internal sealed class DhanFeedPacketParser(TimeProvider clock)
{
    public const int HeaderLength = 8;

    public const byte TickerCode = 2;

    public const byte QuoteCode = 4;

    public const byte PreviousCloseCode = 6;

    public const byte DisconnectCode = 50;

    public const int TickerLength = 16;

    public const int QuoteLength = 50;

    public const int PreviousCloseLength = 16;

    public const int DisconnectLength = 10;

    /// <summary>Numbers beyond this are not prices (a price that would not fit the column).</summary>
    private const decimal Sane = 1_000_000_000m;

    private static readonly TimeSpan MaxTimeSkew = TimeSpan.FromDays(1);

    /// <summary>
    /// Reads the next packet of a frame (a WebSocket message): Dhan packs several packets back to back in one frame (observed 2026-10-14: 50 + 50 + 16 bytes), and the length field of each header is the length of that packet including the
    /// header. Returns how many bytes it used. A header whose declared length is below 8 or beyond what is left, or fewer than 8 bytes left, cannot be walked on: the rest of the frame is used up and counted as one drop
    /// (<see cref="FeedDropReason.LengthMismatch"/>, or <see cref="FeedDropReason.TooShort"/> for a remainder shorter than a header). A packet that is whole but unusable (unknown code, wrong size for its code, bad price) is dropped and the walk goes on after it.
    /// <paramref name="declared"/> is the length field when there were bytes enough for a header. No allocation: spans only.
    /// </summary>
    public int ParseNext(ReadOnlySpan<byte> rest, out FeedPacket? packet, out FeedDropReason reason, out int? declared)
    {
        packet = null;
        declared = null;
        if (rest.Length < HeaderLength)
        {
            reason = FeedDropReason.TooShort;
            return rest.Length;
        }

        var length = BinaryPrimitives.ReadInt16LittleEndian(rest[1..]);
        declared = length;
        if (length < HeaderLength || length > rest.Length)
        {
            reason = FeedDropReason.LengthMismatch;
            return rest.Length;
        }

        _ = TryParse(rest[..length], out packet, out reason);
        return length;
    }

    /// <summary>Parses one message that is exactly one packet. On success <paramref name="packet"/> is set; otherwise <paramref name="reason"/> says why it was dropped.</summary>
    public bool TryParse(ReadOnlySpan<byte> message, out FeedPacket? packet, out FeedDropReason reason)
    {
        packet = null;
        if (message.Length < HeaderLength)
        {
            reason = FeedDropReason.TooShort;
            return false;
        }

        var code = message[0];
        var expected = code switch
        {
            TickerCode => TickerLength,
            QuoteCode => QuoteLength,
            PreviousCloseCode => PreviousCloseLength,
            DisconnectCode => DisconnectLength,
            _ => 0,
        };
        if (expected == 0)
        {
            reason = FeedDropReason.UnknownCode;
            return false;
        }

        if (message.Length != expected || BinaryPrimitives.ReadInt16LittleEndian(message[1..]) != expected)
        {
            reason = FeedDropReason.LengthMismatch;
            return false;
        }

        var securityId = BinaryPrimitives.ReadInt32LittleEndian(message[4..]);
        if (code == DisconnectCode)
        {
            packet = new FeedPacket(FeedPacketKind.Disconnect, default, Reason: BinaryPrimitives.ReadInt16LittleEndian(message[HeaderLength..]));
            reason = FeedDropReason.None;
            return true;
        }

        var segment = message[3] switch
        {
            0 => FeedSegments.Index,
            1 => FeedSegments.NseEquity,
            _ => null,
        };
        if (segment is null || securityId <= 0)
        {
            reason = FeedDropReason.UnknownSegment;
            return false;
        }

        var security = new FeedSecurity(segment, securityId);
        if (!TryPrice(message[HeaderLength..], out var price))
        {
            reason = FeedDropReason.BadPrice;
            return false;
        }

        switch (code)
        {
            case PreviousCloseCode:
                packet = new FeedPacket(FeedPacketKind.PreviousClose, security, price);
                break;
            case TickerCode:
                if (!TryTime(BinaryPrimitives.ReadInt32LittleEndian(message[12..]), out var tickerTime))
                {
                    reason = FeedDropReason.BadTime;
                    return false;
                }

                packet = new FeedPacket(FeedPacketKind.Ticker, security, price, tickerTime);
                break;
            default:
                if (!TryTime(BinaryPrimitives.ReadInt32LittleEndian(message[14..]), out var quoteTime))
                {
                    reason = FeedDropReason.BadTime;
                    return false;
                }

                var volume = BinaryPrimitives.ReadInt32LittleEndian(message[22..]);
                packet = new FeedPacket(
                    FeedPacketKind.Quote,
                    security,
                    price,
                    quoteTime,
                    volume > 0 ? volume : null,
                    OptionalPrice(message[34..]),
                    OptionalPrice(message[42..]),
                    OptionalPrice(message[46..]),
                    OptionalPrice(message[38..]));
                break;
        }

        reason = FeedDropReason.None;
        return true;
    }

    private static bool TryPrice(ReadOnlySpan<byte> payload, out decimal price)
    {
        price = 0m;
        var value = BinaryPrimitives.ReadSingleLittleEndian(payload);
        if (!float.IsFinite(value) || value <= 0f || value >= (float)Sane)
        {
            return false;
        }

        price = Math.Round((decimal)(double)value, 4, MidpointRounding.AwayFromZero); // through double: the direct float-to-decimal cast keeps only seven significant digits (140123.45f would become 140123.5)
        return price > 0m;
    }

    private static decimal? OptionalPrice(ReadOnlySpan<byte> payload) => TryPrice(payload, out var price) ? price : null;

    private bool TryTime(int epochSeconds, out DateTimeOffset time)
    {
        time = default;
        if (epochSeconds <= 0)
        {
            return false;
        }

        time = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        return (time - clock.GetUtcNow()).Duration() <= MaxTimeSkew;
    }
}
