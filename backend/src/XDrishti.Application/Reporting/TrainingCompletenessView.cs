namespace XDrishti.Application.Reporting;

/// <summary>
/// What a report over a simulate-everything job says about itself (spec AU-14, ADR 0243, ADR 0250): that it is a training simulation and not evidence, how much of the job it could read at the as-of instant, and what part of the asked range it could not answer.
/// Only finished chunks are read; while the job runs the figures change as chunks finish.
/// </summary>
/// <param name="Kind">Always <c>training_simulation</c>.</param>
/// <param name="Label">The sentence a screen shows beside every figure: a training simulation, not evidence.</param>
/// <param name="SimJob">The job read.</param>
/// <param name="JobState">running, paused, completed, completed_with_failures or cancelled.</param>
/// <param name="Complete">True only when the job is final, every chunk is done and none failed: the report then holds the whole job; false while it runs, after a failure and after a cancel.</param>
/// <param name="ChunksDone">Chunks finished (the ones this report reads).</param>
/// <param name="ChunksTotal">Chunks of the job.</param>
/// <param name="ChunksFailed">Chunks that failed (never read).</param>
/// <param name="ChunksRunning">Chunks being run now (not read yet).</param>
/// <param name="ChunksPending">Chunks not started.</param>
/// <param name="AsOf">The instant of the read: a chunk finished after it is not in the figures.</param>
/// <param name="WindowFrom">First date of the job's training window.</param>
/// <param name="WindowTo">Last date of the job's training window.</param>
/// <param name="HoldOutStart">First date of the locked hold-out block: no number from it is read, ever.</param>
/// <param name="RangeFrom">First entry date actually read; null when the asked range holds none of the window.</param>
/// <param name="RangeTo">Last entry date actually read; null when the asked range holds none of the window.</param>
/// <param name="RangeCut">The asked range reached past the window (into the hold-out block or beyond): the answer is the part before it.</param>
/// <param name="Locked">The sentence that says the rest is locked, when the asked range reached into the hold-out block; else null.</param>
/// <param name="SymbolsWithoutResults">Stocks asked for with no trade in the finished chunks within the range (not run yet, not tracked, or no signal); each is named, never silently dropped.</param>
/// <param name="TrainingRecord">The route of the training record of the job (what was trained from this simulation).</param>
public sealed record TrainingCompletenessView(
    string Kind,
    string Label,
    Guid SimJob,
    string JobState,
    bool Complete,
    int ChunksDone,
    int ChunksTotal,
    int ChunksFailed,
    int ChunksRunning,
    int ChunksPending,
    DateTimeOffset AsOf,
    DateOnly WindowFrom,
    DateOnly WindowTo,
    DateOnly HoldOutStart,
    DateOnly? RangeFrom,
    DateOnly? RangeTo,
    bool RangeCut,
    string? Locked,
    IReadOnlyList<string> SymbolsWithoutResults,
    string TrainingRecord)
{
    /// <summary>The kind of every training report.</summary>
    public const string TrainingKind = "training_simulation";

    /// <summary>The label of every training report.</summary>
    public const string TrainingLabel = "Training simulation, not evidence: it never feeds the scorecard, a promotion, a plan or a cell lifecycle.";

    /// <summary>The limits a training report carries in its evidence block, as text.</summary>
    public static IReadOnlyList<string> Limits(DateOnly holdOutStart) =>
    [
        TrainingLabel,
        "Only chunks finished at the as-of instant are read; while the job runs, the figures change as chunks finish, and a stock or basket may be only partly covered.",
        $"The hold-out block from {holdOutStart:yyyy-MM-dd} is locked: no trade reaching it is read and no number from inside it is shown.",
        "The latest stored version of each strategy is replayed over the past, so it was shaped knowing the past: these figures are in-sample, and no claim of out-of-sample performance or of freedom from look-ahead is made.",
        "While the job runs, a trade-list cursor is refused once another chunk finishes (the report it belongs to has changed); read the list again from its first page.",
        "Gross R only; the charge sheet confirmation covers evidence runs, not training simulations.",
    ];
}
