namespace XDrishti.Application.Abstractions.Market.History;

/// <summary>One row of <c>market.backfill_chunks</c>: the India dates <c>[From, To)</c> of one instrument and how the request went.</summary>
public sealed record ChunkRecord(Guid InstrumentId, DateOnly From, DateOnly To, string Status, int Rows, int Attempts, string? Error);

/// <summary>One row of <c>market.backfill_seams</c>: the seam (first CSV bar) the planner and the writer last saw, the overlap probe's outcome and the continuity flag.</summary>
public sealed record SeamRecord(Guid InstrumentId, DateTimeOffset SeamAt, string ProbeStatus, DateTimeOffset? ProbedAt, double? MedianBps, double? WithinMaxPct, string? Detail, bool SeamJump, double? JumpPct);

/// <summary>Chunk counts of one instrument for <c>xd data history</c>.</summary>
public sealed record ChunkCounts(Guid InstrumentId, int Total, int Finished, int Failed);

/// <summary>
/// The backfill's own tables: the resumable chunk ledger and the per-instrument seam record (CF-04 Part B). The seam itself is always read from the stored bars (<see cref="CurrentSeamAsync"/>: the oldest bar with source <c>csv</c>), never from the file log,
/// which an undo can move.
/// </summary>
public interface IBackfillStore
{
    /// <summary>The start of the oldest stored bar whose source is <c>csv</c>; null when the instrument has none.</summary>
    ValueTask<DateTimeOffset?> CurrentSeamAsync(Guid instrumentId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyDictionary<Guid, SeamRecord>> ListSeamsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);

    /// <summary>Inserts or updates the instrument's seam row.</summary>
    ValueTask SaveSeamAsync(SeamRecord seam, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<ChunkRecord>> ListChunksAsync(Guid instrumentId, CancellationToken cancellationToken);

    /// <summary>Makes the ledger of the instrument match the plan: a missing chunk is inserted as <c>planned</c>, a <c>planned</c> chunk that is not in the plan any more (the seam moved, the years changed) is removed; every other row is left alone.</summary>
    ValueTask PlanChunksAsync(Guid instrumentId, IReadOnlyList<(DateOnly From, DateOnly To)> chunks, CancellationToken cancellationToken);

    ValueTask SaveChunkAsync(ChunkRecord chunk, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<ChunkCounts>> CountChunksAsync(CancellationToken cancellationToken);
}

/// <summary>What the simulation reads: where an instrument's backfilled (pre-seam) bars end. Null when it has none.</summary>
public interface IBackfillSeams
{
    ValueTask<DateTimeOffset?> SeamOfAsync(Guid instrumentId, CancellationToken cancellationToken);
}

/// <summary>What the AU-12 entry gate reads: whether the backfill still has work (a running backfill is visible as planned chunks).</summary>
public interface IBackfillProgress
{
    /// <summary>The number of instruments with an unfinished (<c>planned</c> or <c>failed</c>) chunk, not counting those whose seam is unresolved or jumped (they count as complete without pre-seam bars).</summary>
    ValueTask<int> UnfinishedInstrumentsAsync(CancellationToken cancellationToken);
}

/// <summary>The days Part A has a verdict for (a loaded file or a recorded absence): the calendar the backfill needs before it starts (<c>history.calendar_missing</c>).</summary>
public interface IBackfillCalendar
{
    /// <summary>The dates in the closed range <c>[from, to]</c> that have an entry in the daily-file ledger.</summary>
    ValueTask<IReadOnlySet<DateOnly>> KnownDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
