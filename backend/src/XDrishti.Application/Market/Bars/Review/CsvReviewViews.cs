namespace XDrishti.Application.Market.Bars.Review;

/// <summary>The state of a CSV file of the folder. The spelling is part of the API.</summary>
public static class CsvFileStates
{
    /// <summary>Never loaded (or its load was undone).</summary>
    public const string Pending = "pending";

    /// <summary>Loaded, and size and modified time are the same as when it was loaded.</summary>
    public const string Loaded = "loaded";

    /// <summary>Loaded, but size or modified time differ now: the next load reads it again.</summary>
    public const string Changed = "changed";

    public static bool IsKnown(string? state) => state is Pending or Loaded or Changed;
}

/// <summary>How the symbol of a file name maps to the master list. The spelling is part of the API.</summary>
public static class CsvFileMappings
{
    /// <summary>Exactly one share or ETF (not retired) has the symbol and no other file claims it.</summary>
    public const string Mapped = "mapped";

    public const string Unknown = "unknown";

    /// <summary>More than one instrument has the symbol, or more than one file does: the load skips the file, never guesses.</summary>
    public const string Ambiguous = "ambiguous";

    /// <summary>A <c>.csv</c> or <c>.csv.gz</c> file whose name is not <c>&lt;SYMBOL&gt;_1m.csv[.gz]</c>: it is never loaded.</summary>
    public const string InvalidName = "invalidName";
}

/// <summary>The instrument a file maps to: its id and canonical symbol (<c>NSE:ABB</c>).</summary>
public sealed record CsvInstrumentRef(Guid Id, string Symbol);

/// <summary>
/// One file of the CSV folder. <c>state</c>: pending · loaded · changed (loaded, but size or modified time differ now). <c>symbol</c> is parsed from the name (null for an invalid name),
/// <c>mapping</c>: mapped · unknown · ambiguous · invalidName, <c>instrument</c> is set when mapped. <c>rows</c>, <c>firstBar</c>, <c>lastBar</c> (null for a file loaded before the range was recorded) and
/// <c>loadedAt</c> are set for loaded and changed files. <c>overlapsWith</c> (loaded and changed files; null for pending ones): the names of the other loaded files of the same instrument that cover part
/// of the same time — undoing either removes the instrument's CSV bars in the overlap, whichever file wrote them.
/// </summary>
public sealed record CsvFileRow(
    string Name,
    string State,
    long Size,
    DateTimeOffset ModifiedAt,
    string? Symbol,
    string Mapping,
    CsvInstrumentRef? Instrument,
    long? Rows,
    DateTimeOffset? FirstBar,
    DateTimeOffset? LastBar,
    DateTimeOffset? LoadedAt,
    IReadOnlyList<string>? OverlapsWith = null);

/// <summary>The files by state over the whole folder (not narrowed by <c>state</c>, but by <c>q</c>), and how many cannot be loaded (<c>unmapped</c>: unknown, ambiguous or invalid name).</summary>
public sealed record CsvFileCounts(int Total, int Pending, int Loaded, int Changed, int Unmapped);

/// <summary>A page of the folder's files: changed and pending first, then loaded, by name; <c>total</c> is the number after the filters.</summary>
public sealed record CsvFilePage(string Folder, bool FolderExists, IReadOnlyList<CsvFileRow> Items, int Page, int PageSize, int Total, CsvFileCounts Counts);

/// <summary>The list as received (query string values, unchecked).</summary>
public sealed record CsvFileRequest(string? Q, string? State, int? Page, int? PageSize);

/// <summary>The outcome of previewing one file. The spelling is part of the API.</summary>
public static class CsvPreviewVerdicts
{
    /// <summary>The file was read and its times fit the trading sessions: loading it would store its bars.</summary>
    public const string Pass = "pass";

    /// <summary>The file was read but would fail as a whole when loaded (wrong clock, wrong time unit, a limit, no usable header); <c>message</c> says why and nothing would be stored.</summary>
    public const string Refused = "refused";

    /// <summary>The file could not be previewed (not in the folder, an invalid name, unreadable, the request's limit); <c>message</c> says why.</summary>
    public const string Error = "error";
}

/// <summary>An accepted bar, as the load would store it (after repair), with its start in India time.</summary>
public sealed record CsvPreviewBar(DateTimeOffset Time, double Open, double High, double Low, double Close, long Volume);

/// <summary>
/// What loading the file would do to the stored bars of its instrument, in bars: <c>wouldInsert</c> (minute not stored yet), <c>wouldUpdate</c> (a CSV bar of that minute with other values),
/// <c>keepExistingDhan</c> (a Dhan bar of that minute is kept: CSV never overwrites Dhan) and <c>unchanged</c> (same values). Counted over <c>comparedRows</c> accepted rows; when the file has more
/// accepted rows than the request may compare (<c>sampled</c>), a spread sample of whole batches is compared and the counts are of that sample, not scaled.
/// </summary>
public sealed record CsvPreviewComparison(long WouldInsert, long WouldUpdate, long KeepExistingDhan, long Unchanged, long ComparedRows, bool Sampled);

/// <summary>
/// The dry run of the real reader over one file, nothing written. <c>verdict</c>: pass · refused · error (see <c>message</c>). <c>rows</c> are the data rows, <c>accepted</c> the ones that would be stored (<c>repaired</c> among them),
/// <c>rejected</c> the others, counted by reason in <c>rejections</c> with up to 10 <c>examples</c> (line numbers and reasons, never row values). <c>tradingDays</c> counts the distinct India dates of the accepted bars.
/// <c>wouldLoad</c>: passes and maps to exactly one instrument. <c>firstRows</c>: the first 5 accepted bars. <c>comparison</c> is null when the file does not pass or does not map.
/// </summary>
public sealed record CsvFilePreview(
    string Name,
    string Verdict,
    string? Message,
    string? State,
    string? Symbol,
    string? Mapping,
    CsvInstrumentRef? Instrument,
    bool WouldLoad,
    long Rows,
    long Accepted,
    long Repaired,
    long Rejected,
    DateTimeOffset? FirstBar,
    DateTimeOffset? LastBar,
    int TradingDays,
    IReadOnlyDictionary<string, long> Rejections,
    IReadOnlyList<string> Examples,
    IReadOnlyList<CsvPreviewBar> FirstRows,
    CsvPreviewComparison? Comparison);

/// <summary>The previews in the order the files were asked for; <c>rowsRead</c> and <c>comparedRows</c> are the totals of the request (its limits are in the summary of the endpoint).</summary>
public sealed record CsvPreviewResult(IReadOnlyList<CsvFilePreview> Files, long RowsRead, long ComparedRows);

/// <summary>The files to preview: names from the folder listing, at most <see cref="PreviewCsvFilesHandler.MaxFiles"/>.</summary>
public sealed record PreviewCsvFilesRequest(IReadOnlyList<string>? Files);
