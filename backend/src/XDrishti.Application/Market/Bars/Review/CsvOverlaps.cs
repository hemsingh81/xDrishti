using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Application.Market.Bars.Review;

/// <summary>
/// Which loaded CSV files of one instrument cover the same time (a file copied under another name, say). The undo of a load removes the instrument's CSV bars within the file's first-to-last bar, whichever file
/// wrote them, so it also removes what an overlapping file wrote there; the files stay loaded. A file loaded before its range was recorded counts as covering everything (the undo works on the instrument's whole
/// range for it).
/// </summary>
internal static class CsvOverlaps
{
    /// <summary>The names of the loaded files of the same instrument that overlap <paramref name="file"/>, by name.</summary>
    public static IReadOnlyList<string> Of(LoadedCsvFile file, IEnumerable<LoadedCsvFile> loaded)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(loaded);
        return file.InstrumentId is null
            ? []
            : [.. loaded.Where(other => !string.Equals(other.Name, file.Name, StringComparison.Ordinal) && Overlap(file, other)).Select(other => other.Name).Order(StringComparer.Ordinal)];
    }

    /// <summary>For every loaded file the names it overlaps; files that overlap nothing map to an empty list.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> All(IReadOnlyCollection<LoadedCsvFile> loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        var overlaps = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var group in loaded.Where(f => f.InstrumentId is not null).GroupBy(f => f.InstrumentId))
        {
            var files = group.ToList();
            foreach (var file in files)
            {
                overlaps[file.Name] = Of(file, files);
            }
        }

        return overlaps;
    }

    private static bool Overlap(LoadedCsvFile a, LoadedCsvFile b) =>
        a.InstrumentId == b.InstrumentId
        && (a.FirstBar is null || b.LastBar is null || a.FirstBar <= b.LastBar)
        && (b.FirstBar is null || a.LastBar is null || b.FirstBar <= a.LastBar);
}
