using System.Buffers.Binary;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>
/// Constructed frames of Dhan's 20-level depth feed, written from the documentation (https://dhanhq.co/docs/v2/full-market-depth/): little endian, a 12-byte header (message length int16, response code, segment, security id int32,
/// sequence uint32) and 20 levels of 16 bytes (price float64, quantity uint32, orders uint32). <b>UNVERIFIED</b> until the first real session; they must be replaced by sanitised captures (docs/research/dhan-samples/depth20-*.hex).
/// </summary>
internal static class DepthPackets
{
    public const int Levels = 20;

    public static byte[] Book(byte code, int securityId, uint sequence, double bestPrice, double step, int filled = Levels, byte segment = 1, int? length = null)
    {
        var size = length ?? 12 + (Levels * 16);
        var packet = new byte[Math.Max(size, 12 + (Levels * 16))];
        BinaryPrimitives.WriteInt16LittleEndian(packet, (short)size);
        packet[2] = code;
        packet[3] = segment;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), securityId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), sequence);
        for (var i = 0; i < filled; i++)
        {
            var level = packet.AsSpan(12 + (i * 16));
            BinaryPrimitives.WriteDoubleLittleEndian(level, bestPrice + (step * i));
            BinaryPrimitives.WriteUInt32LittleEndian(level[8..], (uint)(100 + i));
            BinaryPrimitives.WriteUInt32LittleEndian(level[12..], (uint)(2 + i));
        }

        return size == packet.Length ? packet : packet[..size];
    }

    public static byte[] Bid(int securityId, double best = 100.00, uint sequence = 1) => Book(41, securityId, sequence, best, -0.05);

    public static byte[] Ask(int securityId, double best = 100.05, uint sequence = 2) => Book(51, securityId, sequence, best, 0.05);

    /// <summary>A disconnect packet: the 12-byte header and the reason int16.</summary>
    public static byte[] Disconnect(short reason)
    {
        var packet = new byte[14];
        BinaryPrimitives.WriteInt16LittleEndian(packet, 14);
        packet[2] = 50;
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(12), reason);
        return packet;
    }

    /// <summary>A frame of several packets back to back.</summary>
    public static byte[] Frame(params byte[][] packets) => [.. packets.SelectMany(p => p)];

    public static void SetPrice(byte[] packet, int level, double price) => BinaryPrimitives.WriteDoubleLittleEndian(packet.AsSpan(12 + (level * 16)), price);
}
