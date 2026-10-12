namespace XDrishti.Domain.Market.Bars;

/// <summary>
/// The CSV phase of a data job (ADR 0016 §4): <c>Done</c> of <c>Total</c> are files. Rows are counted over all files read in this job. Part of the API
/// (the job's <c>csv</c> block) and stored with the job.
/// </summary>
public sealed record CsvPhase(
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    long Done,
    long Total,
    string? Message,
    string? Code,
    long FilesLoaded,
    long FilesUnchanged,
    long FilesSkipped,
    long FilesFailed,
    long Rows,
    long Inserted,
    long Updated,
    long Unchanged,
    long KeptDhan,
    long Repaired,
    long Rejected,
    IReadOnlyDictionary<string, long> Rejections,
    IReadOnlyList<string> RejectionExamples,
    IReadOnlyList<string> UnknownSymbols,
    IReadOnlyList<string> AmbiguousSymbols,
    IReadOnlyList<string> FailedFiles,
    int InstrumentsTracked) : IDataPhase
{
    /// <summary>How many examples of rejected rows, unknown symbols and failed files are kept.</summary>
    public const int MaxExamples = 10;

    /// <summary>How many unknown or ambiguous symbols are listed.</summary>
    public const int MaxSymbols = 200;

    public static CsvPhase Empty(string status) => new(
        status, null, null, 0, 0, null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        new Dictionary<string, long>(), [], [], [], [], 0);
}
