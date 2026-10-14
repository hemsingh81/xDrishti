using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>Builds binary packets of Dhan's market feed (little-endian, as documented) and reads the constructed fixtures of docs/research/dhan-samples. UNVERIFIED layouts.</summary>
internal static class FeedPackets
{
    /// <summary>The instant of the fixtures: 2026-10-12 05:00:00 UTC (10:30 India time).</summary>
    public static readonly DateTimeOffset FixtureTime = new(2026, 10, 12, 5, 0, 0, TimeSpan.Zero);

    public static int Epoch(DateTimeOffset at) => (int)at.ToUnixTimeSeconds();

    public static byte[] Header(byte code, int length, byte segment, int securityId)
    {
        var bytes = new byte[length];
        bytes[0] = code;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(1), (short)length);
        bytes[3] = segment;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), securityId);
        return bytes;
    }

    public static byte[] Ticker(byte segment, int securityId, float price, int epoch)
    {
        var bytes = Header(2, 16, segment, securityId);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8), price);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), epoch);
        return bytes;
    }

    public static byte[] Quote(byte segment, int securityId, float price, int epoch, int volume = 1_000, float open = 10f, float close = 11f, float high = 12f, float low = 9f)
    {
        var bytes = Header(4, 50, segment, securityId);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8), price);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(12), 25);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), epoch);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(18), price);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), volume);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(26), 5_000);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(30), 6_000);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(34), open);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(38), close);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(42), high);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(46), low);
        return bytes;
    }

    public static byte[] PreviousClose(byte segment, int securityId, float price)
    {
        var bytes = Header(6, 16, segment, securityId);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8), price);
        return bytes;
    }

    public static byte[] Disconnect(short reason)
    {
        var bytes = Header(50, 10, 0, 0);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(8), reason);
        return bytes;
    }

    /// <summary>A fixture of docs/research/dhan-samples: hex bytes in a text, <c>#</c> lines are comments.</summary>
    public static byte[] Fixture(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DhanFeed/" + name) ?? throw new InvalidOperationException("No fixture " + name);
        using var reader = new StreamReader(stream);
        var hex = reader.ReadToEnd().Split('\n').Where(line => !line.TrimStart().StartsWith('#')).SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return [.. hex.Select(b => byte.Parse(b, NumberStyles.HexNumber, CultureInfo.InvariantCulture))];
    }
}
