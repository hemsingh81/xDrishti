using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Csv;

namespace XDrishti.Application.Market.Bars.Review;

/// <summary>A file of the folder with what the log and the master list say about it. <see cref="Loaded"/> is set for a loaded or changed file.</summary>
internal sealed record CsvFileEntry(CsvFileInfo File, string State, string? Symbol, string Mapping, CsvInstrumentRef? Instrument, LoadedCsvFile? Loaded);

/// <summary>The folder as the review screens see it: <see cref="Exists"/>, and an entry for every <c>.csv</c> or <c>.csv.gz</c> file (nothing else is a candidate).</summary>
/// <remarks><see cref="Overlaps"/>: for every loaded file the names of the other loaded files of its instrument that cover part of the same time (see <see cref="CsvOverlaps"/>).</remarks>
internal sealed record CsvFolderListing(bool Exists, IReadOnlyList<CsvFileEntry> Files, IReadOnlyDictionary<string, IReadOnlyList<string>>? Overlaps = null);

/// <summary>
/// Joins the folder's directory entries (no file is opened: 214 files list in milliseconds), the log of loaded files and the master list into the review view. The rules are the CSV phase's: the symbol of
/// <c>&lt;SYMBOL&gt;_1m.csv[.gz]</c> becomes <c>NSE:&lt;SYMBOL&gt;</c> and must match exactly one share or ETF that is not retired, and no other file may claim the same symbol.
/// </summary>
internal sealed class CsvFileCatalog(ICsvFolder folder, ICsvFileLog log, IListedInstruments listed)
{
    public async ValueTask<CsvFolderListing> ListAsync(CancellationToken cancellationToken)
    {
        if (!folder.Exists())
        {
            return new CsvFolderListing(false, []);
        }

        IReadOnlyList<CsvFileInfo> all;
        try
        {
            all = await folder.ListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CsvFolderListing(true, []); // an unreadable mount lists as empty; the CSV phase reports the problem
        }

        var candidates = all.Where(IsCandidate).ToList();
        var symbolOf = candidates
            .Select(f => (File: f, Symbol: CsvFileName.TryParse(f.Name, out var symbol) ? symbol : null))
            .ToList();
        var canonical = symbolOf.Where(x => x.Symbol is not null).Select(x => CsvFileName.Canonical(x.Symbol!)).Distinct(StringComparer.Ordinal).ToList();
        var matches = canonical.Count == 0 ? new Dictionary<string, IReadOnlyList<Guid>>() : await listed.FindAsync(canonical, cancellationToken);
        var filesPerSymbol = symbolOf.Where(x => x.Symbol is not null).GroupBy(x => CsvFileName.Canonical(x.Symbol!), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var loaded = await log.ListAsync(cancellationToken);

        var entries = new List<CsvFileEntry>(candidates.Count);
        foreach (var (file, symbol) in symbolOf)
        {
            var previous = loaded.GetValueOrDefault(file.Name);
            var state = previous is null ? CsvFileStates.Pending : previous.Size == file.Size && previous.ModifiedAt == file.ModifiedAt ? CsvFileStates.Loaded : CsvFileStates.Changed;
            if (symbol is null)
            {
                entries.Add(new CsvFileEntry(file, state, null, CsvFileMappings.InvalidName, null, previous));
                continue;
            }

            var key = CsvFileName.Canonical(symbol);
            if (!matches.TryGetValue(key, out var ids))
            {
                entries.Add(new CsvFileEntry(file, state, symbol, CsvFileMappings.Unknown, null, previous));
            }
            else if (ids.Count != 1 || filesPerSymbol[key] != 1)
            {
                entries.Add(new CsvFileEntry(file, state, symbol, CsvFileMappings.Ambiguous, null, previous));
            }
            else
            {
                entries.Add(new CsvFileEntry(file, state, symbol, CsvFileMappings.Mapped, new CsvInstrumentRef(ids[0], key), previous));
            }
        }

        return new CsvFolderListing(true, entries, CsvOverlaps.All([.. loaded.Values]));
    }

    private static bool IsCandidate(CsvFileInfo file) =>
        file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase);
}
