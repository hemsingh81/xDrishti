using System.IO.Compression;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// Looks at an <c>.xlsx</c> package (a zip) before it is parsed, against a decompression bomb (ADR 0030): a small file whose entries declare far more than they hold would be expanded in memory by the XML readers. The package is refused when it has more than
/// <see cref="MaxEntries"/> entries, declares more than <see cref="MaxUncompressedBytes"/> bytes in all, or has an entry of at least <see cref="RatioFloorBytes"/> bytes that expands more than <see cref="MaxRatio"/> times. Only the central directory is read: nothing is decompressed. A stream that
/// is not a zip is none of this guard's business (the reader refuses it as unreadable); a corrupt central directory is reported as <see cref="InvalidDataException"/>. Pure apart from reading the stream.
/// </summary>
/// <remarks>
/// Assumptions: the sizes are the ones <b>declared</b> in the central directory (a package that understates them in its directory is not caught by this guard; what is actually read is bounded only by the row and cell limits of the reader);
/// the entry cap counts every part of the package, including folders and relationship files, not only worksheets; the stream must be seekable (<see cref="SpreadsheetReader"/> refuses any other, so the guard is never skipped).
/// </remarks>
internal static class SpreadsheetPackageGuard
{
    /// <summary>A real workbook has around a dozen entries (content types, relationships, workbook, styles, sheets, shared strings).</summary>
    public const int MaxEntries = 50;

    /// <summary>The most all entries together may declare: 100 MB, far above a sheet of <see cref="SpreadsheetReader.MaxCells"/> cells.</summary>
    public const long MaxUncompressedBytes = 100L * 1024 * 1024;

    /// <summary>The most one entry may expand (uncompressed ÷ compressed): XML of repeated cells compresses well, but not 200 times that.</summary>
    public const int MaxRatio = 200;

    /// <summary>Entries below this size are not looked at for their ratio (a tiny part that happens to compress well is no danger; the total is capped anyway).</summary>
    public const long RatioFloorBytes = 1024 * 1024;

    private static readonly byte[] ZipSignature = [(byte)'P', (byte)'K', 3, 4];

    /// <summary>True when the stream starts like a zip package (it is positioned at 0 afterwards).</summary>
    public static bool IsPackage(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var head = new byte[ZipSignature.Length];
        content.Position = 0;
        var read = content.Read(head, 0, head.Length);
        content.Position = 0;
        return read == head.Length && head.AsSpan().SequenceEqual(ZipSignature);
    }

    /// <summary>Null when the package is within the limits, else a plain description of what is too much. The stream is positioned at 0 afterwards.</summary>
    public static string? Problem(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaxEntries)
            {
                return $"more than {MaxEntries} parts";
            }

            long total = 0;
            foreach (var entry in archive.Entries)
            {
                total += entry.Length;
                if (total > MaxUncompressedBytes)
                {
                    return $"more than {MaxUncompressedBytes / (1024 * 1024)} MB once unpacked";
                }

                if (entry.Length >= RatioFloorBytes && entry.Length > (long)MaxRatio * Math.Max(1, entry.CompressedLength))
                {
                    return "a part that unpacks to far more than it holds";
                }
            }

            return null;
        }
        finally
        {
            content.Position = 0;
        }
    }
}
