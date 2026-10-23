using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XDrishti.Learning.MlNet;

/// <summary>
/// The container of a model artifact: the 8 bytes "XDMODEL1", the length of the header as a little-endian int32, the header as JSON, then the ML.NET model zip. The zip is re-packed with sorted entries and a fixed timestamp
/// so that the same trained model always gives the same bytes and the same hash; entries are stored without compression so that the bytes do not depend on the deflate implementation of the runtime.
/// </summary>
internal static class ArtifactCodec
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("XDMODEL1");
    /// <summary>The most bytes an artifact may have (a model of 100 trees is about 100 KB).</summary>
    public const int MaxArtifactBytes = 64 * 1024 * 1024;

    /// <summary>The most bytes the header JSON may have.</summary>
    public const int MaxHeaderBytes = 1024 * 1024;

    /// <summary>The most bytes the model zip may hold once unpacked, and the most entries it may have.</summary>
    public const long MaxUnpackedBytes = 256L * 1024 * 1024;

    private const int MaxEntries = 64;

    private static readonly DateTimeOffset FixedTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static ModelArtifact Pack(ArtifactHeader header, byte[] mlNetModel)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(header);
        using var stream = new MemoryStream();
        stream.Write(Magic);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, json.Length);
        stream.Write(length);
        stream.Write(json);
        stream.Write(Normalise(mlNetModel));
        var bytes = stream.ToArray();
        return new ModelArtifact(bytes, Hash(bytes));
    }

    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>The header and the ML.NET model bytes, or null when the bytes are not an artifact.</summary>
    public static (ArtifactHeader Header, byte[] Model)? Unpack(byte[] bytes)
    {
        if (bytes.Length > MaxArtifactBytes || bytes.Length < Magic.Length + 4 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            return null;
        }

        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(Magic.Length, 4));
        var start = Magic.Length + 4;
        if (length < 2 || length > MaxHeaderBytes || length > bytes.Length - start)
        {
            return null;
        }

        try
        {
            var header = JsonSerializer.Deserialize<ArtifactHeader>(bytes.AsSpan(start, length));
            return header is null ? null : (header, bytes[(start + length)..]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>True when the bytes are a zip of a few entries that unpack to a sane size.</summary>
    public static bool ZipIsSane(byte[] zipBytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
            return zip.Entries.Count is > 0 and <= MaxEntries && zip.Entries.Sum(e => e.Length) <= MaxUnpackedBytes;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static byte[] Normalise(byte[] zipBytes)
    {
        using var input = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                var copy = zip.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                copy.LastWriteTime = FixedTime;
                using var source = entry.Open();
                using var target = copy.Open();
                source.CopyTo(target);
            }
        }

        return output.ToArray();
    }
}
