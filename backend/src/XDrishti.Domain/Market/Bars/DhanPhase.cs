namespace XDrishti.Domain.Market.Bars;

/// <summary>
/// The Dhan phase of a data job (ADR 0016 §2, §7): <c>Done</c> of <c>Total</c> are instruments. <c>Bars</c> are the 1-minute bars Dhan returned;
/// <c>Rejected</c> are bars that did not lie on an IST minute inside a session or broke the row rules; <c>OverlapCompared</c> and
/// <c>OverlapMismatched</c> compare the overlap with the bars already stored (tolerance 0.05 %). <c>GapsFound</c> counts the instrument-sessions with missing bars
/// this phase planned to refetch or found still incomplete, <c>GapsFilled</c> those that now have all their bars and <c>GapsUnfillable</c> those a refetch added nothing to
/// (ADR 0017 §3). Part of the API (the job's <c>dhan</c> block).
/// </summary>
public sealed record DhanPhase(
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    long Done,
    long Total,
    string? Message,
    string? Code,
    string? AccountName,
    long InstrumentsFetched,
    long InstrumentsFailed,
    long Requests,
    long Bars,
    long Inserted,
    long Updated,
    long Unchanged,
    long Repaired,
    long Rejected,
    long OverlapCompared,
    long OverlapMismatched,
    IReadOnlyList<string> Errors,
    long GapsFound,
    long GapsFilled,
    long GapsUnfillable) : IDataPhase
{
    /// <summary>How many per-instrument error messages are kept.</summary>
    public const int MaxErrors = 10;

    public static DhanPhase Empty(string status) => new(status, null, null, 0, 0, null, null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], 0, 0, 0);
}
