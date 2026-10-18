using System.Text;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Manifest;

/// <summary>
/// The export key (spec P3-04b1, E6): the lower-case hex SHA-256 of the UTF-8 lines <c>format=…</c>, <c>run_key=…</c>, <c>basket=…</c> (the GUID in D form, or <c>-</c>), then <c>file=name|sha256</c> for each entry in ordinal name order,
/// then <c>extra=…</c> for each extra line in the order given, each line ending in <c>\n</c>. It says whether two exports are the same: the same run, the same basket (the run key does not cover it), the same bytes in every file and the same
/// extra lines. It does not cover the length of a file (the hash does) and it does not depend on the order the entries were given in.
/// </summary>
public static class ExportKey
{
    /// <summary>The key of an export.</summary>
    /// <exception cref="ArgumentException">The format is empty or has a line break, the run key is not 64 lower-case hex, an entry is not valid (see <see cref="ExportNames"/>), a name is there twice, or an extra line has a line break.</exception>
    public static string Of(string format, string runKey, Guid? basketId, IReadOnlyList<ManifestEntry> entries, IReadOnlyList<string> extraLines)
    {
        Check(format, runKey, entries, extraLines);

        var text = new StringBuilder();
        text.Append("format=").Append(format).Append('\n');
        text.Append("run_key=").Append(runKey).Append('\n');
        text.Append("basket=").Append(basketId is { } basket ? basket.ToString("D") : "-").Append('\n');
        foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            text.Append("file=").Append(entry.Name).Append('|').Append(entry.Sha256).Append('\n');
        }

        foreach (var line in extraLines)
        {
            text.Append("extra=").Append(line).Append('\n');
        }

        return CanonicalText.Sha256Hex(text.ToString());
    }

    internal static void Check(string format, string runKey, IReadOnlyList<ManifestEntry> entries, IReadOnlyList<string> extraLines)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(extraLines);
        if (format.Length == 0 || HasLineBreak(format))
        {
            throw new ArgumentException("The format must be a non-empty text without a line break.", nameof(format));
        }

        ExportText.RequireRunKey(runKey);
        if (ExportNames.Problem(entries) is { } problem)
        {
            throw new ArgumentException(problem, nameof(entries));
        }

        if (extraLines.Any(line => line is null || HasLineBreak(line)))
        {
            throw new ArgumentException("An extra line must be a text without a line break.", nameof(extraLines));
        }
    }

    internal static bool HasLineBreak(string text) => text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal);
}
