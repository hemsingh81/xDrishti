using System.Buffers.Binary;
using XDrishti.Application.Abstractions.Depth;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>Why a packet of the depth feed was dropped (counted, never thrown).</summary>
internal enum DepthDropReason
{
    None,
    TooShort,
    LengthMismatch,
    UnknownCode,
    UnknownSegment,
    BadPrice,
}

/// <summary>
/// Reads the binary messages of Dhan's 20-level depth feed (P7-16; docs: https://dhanhq.co/docs/v2/full-market-depth/). Little endian. A packet is a 12-byte header - message length int16 (the packet including the header), response code (1 byte),
/// exchange segment (1 byte), security id int32, sequence uint32 - and a body: code 41 (bid) and 51 (ask) carry 20 levels of 16 bytes (price float64, quantity uint32, orders uint32), 332 bytes in all; code 50 is the disconnect with a reason int16
/// after the header. A frame can hold several packets back to back (walked by <see cref="ParseNext"/>, split by the length field). <b>UNVERIFIED</b> until the first real session: the byte order, the header layout (the page's text says
/// "byte 12 is the response code" while its table puts the code at byte 3; this parser follows the table and the length check), the meaning of the sequence and the disconnect layout (the reason is read at byte 12, or at byte 8 for a packet of 10 to 13
/// bytes). Anything that does not fit - a length that does not match the code, an unknown code, a segment other than NSE equity (code 1), a price that is NaN, infinite, negative or too large for the paise column, a tail too short for a packet - is dropped
/// with a reason and counted by the caller; one bad packet never ends the connection. No allocation except the packet itself.
/// </summary>
internal static class DhanDepth20Parser
{
    public const int HeaderLength = 12;

    public const byte BidCode = 41;

    public const byte AskCode = 51;

    public const byte DisconnectCode = 50;

    public const int LevelLength = 16;

    public const int BookLength = HeaderLength + (DepthPacket.Levels * LevelLength);

    /// <summary>Dhan's number of the NSE equity segment.</summary>
    public const byte NseEquitySegment = 1;

    /// <summary>A price above this (in rupees) does not fit the paise column; it is not a price.</summary>
    public const double MaxPrice = 21_000_000d;

    /// <summary>
    /// Reads the next packet of a frame and returns how many bytes it used. A length that is below 4 or beyond what is left cannot be walked on: the rest of the frame is used up and counted as one drop (<see cref="DepthDropReason.LengthMismatch"/>,
    /// or <see cref="DepthDropReason.TooShort"/> for a remainder shorter than a length field). A whole packet that is unusable is dropped and the walk goes on after it.
    /// </summary>
    public static int ParseNext(ReadOnlySpan<byte> rest, out DepthPacket? packet, out DepthDropReason reason)
    {
        packet = null;
        if (rest.Length < 4)
        {
            reason = DepthDropReason.TooShort;
            return rest.Length;
        }

        var length = BinaryPrimitives.ReadInt16LittleEndian(rest);
        if (length < 4 || length > rest.Length)
        {
            reason = DepthDropReason.LengthMismatch;
            return rest.Length;
        }

        _ = TryParse(rest[..length], out packet, out reason);
        return length;
    }

    /// <summary>Parses one message that is exactly one packet.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out DepthPacket? packet, out DepthDropReason reason)
    {
        packet = null;
        if (message.Length < 4 || BinaryPrimitives.ReadInt16LittleEndian(message) != message.Length)
        {
            reason = DepthDropReason.LengthMismatch;
            return false;
        }

        var code = message[2];
        if (code == DisconnectCode)
        {
            if (message.Length < 10)
            {
                reason = DepthDropReason.LengthMismatch;
                return false;
            }

            var at = message.Length >= HeaderLength + 2 ? HeaderLength : 8;
            packet = DepthPacket.Disconnect(BinaryPrimitives.ReadInt16LittleEndian(message[at..]));
            reason = DepthDropReason.None;
            return true;
        }

        if (code is not (BidCode or AskCode))
        {
            reason = DepthDropReason.UnknownCode;
            return false;
        }

        if (message.Length != BookLength)
        {
            reason = DepthDropReason.LengthMismatch;
            return false;
        }

        var securityId = BinaryPrimitives.ReadInt32LittleEndian(message[4..]);
        if (message[3] != NseEquitySegment || securityId <= 0)
        {
            reason = DepthDropReason.UnknownSegment;
            return false;
        }

        var prices = new double[DepthPacket.Levels];
        var quantities = new uint[DepthPacket.Levels];
        var orders = new uint[DepthPacket.Levels];
        for (var i = 0; i < DepthPacket.Levels; i++)
        {
            var level = message[(HeaderLength + (i * LevelLength))..];
            var price = BinaryPrimitives.ReadDoubleLittleEndian(level);
            if (!double.IsFinite(price) || price < 0 || price > MaxPrice)
            {
                reason = DepthDropReason.BadPrice;
                return false;
            }

            prices[i] = price;
            quantities[i] = BinaryPrimitives.ReadUInt32LittleEndian(level[8..]);
            orders[i] = BinaryPrimitives.ReadUInt32LittleEndian(level[12..]);
        }

        packet = new DepthPacket(code == BidCode ? DepthPacketKind.Bid : DepthPacketKind.Ask, securityId, BinaryPrimitives.ReadUInt32LittleEndian(message[8..]), prices, quantities, orders);
        reason = DepthDropReason.None;
        return true;
    }
}
