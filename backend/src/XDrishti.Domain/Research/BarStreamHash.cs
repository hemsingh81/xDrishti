using System.Buffers.Binary;
using System.Security.Cryptography;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Research;

/// <summary>
/// The hash of the stored bars a replay read (spec P3-04 Scope 9): the SHA-256, lower-case hex, of the concatenation, for each raw bar in order, of six 8-byte little-endian fields: <c>EpochSeconds</c>, the 64-bit patterns of
/// <c>Open</c>, <c>High</c>, <c>Low</c> and <c>Close</c>, then <c>Volume</c>. It covers the bars as stored, before any session is dropped and before adjustment, so a backfill or a merge that changes one price by one tick changes it.
/// </summary>
public static class BarStreamHash
{
    private const int BytesPerBar = 48;

    private const int BarsPerChunk = 128;

    /// <summary>The hash of the bars; the hash of no bars is that of the empty input (<c>e3b0c442…</c>).</summary>
    public static string Compute(IReadOnlyList<Bar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> chunk = stackalloc byte[BytesPerBar * BarsPerChunk];
        var filled = 0;
        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            var at = filled * BytesPerBar;
            BinaryPrimitives.WriteInt64LittleEndian(chunk[at..], bar.EpochSeconds);
            BinaryPrimitives.WriteInt64LittleEndian(chunk[(at + 8)..], BitConverter.DoubleToInt64Bits(bar.Open));
            BinaryPrimitives.WriteInt64LittleEndian(chunk[(at + 16)..], BitConverter.DoubleToInt64Bits(bar.High));
            BinaryPrimitives.WriteInt64LittleEndian(chunk[(at + 24)..], BitConverter.DoubleToInt64Bits(bar.Low));
            BinaryPrimitives.WriteInt64LittleEndian(chunk[(at + 32)..], BitConverter.DoubleToInt64Bits(bar.Close));
            BinaryPrimitives.WriteInt64LittleEndian(chunk[(at + 40)..], bar.Volume);
            if (++filled == BarsPerChunk)
            {
                hash.AppendData(chunk);
                filled = 0;
            }
        }

        if (filled > 0)
        {
            hash.AppendData(chunk[..(filled * BytesPerBar)]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
