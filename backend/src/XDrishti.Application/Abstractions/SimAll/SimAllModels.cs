namespace XDrishti.Application.Abstractions.SimAll;

/// <summary>A simulate-everything job (spec AU-12): one window for every chunk, the workers it was started with and its state.</summary>
public sealed record SimAllJob(
    Guid Id,
    string State,
    string? PauseReason,
    DateOnly WindowFrom,
    DateOnly WindowTo,
    DateOnly HoldOutStart,
    int Workers,
    string StartedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt);

/// <summary>A chunk to create: one strategy version over one instrument. <paramref name="Ordinal"/> is its place in the work order (strategy, then instrument).</summary>
public sealed record NewSimAllChunk(string StrategyId, int Version, string StrategyHash, Guid InstrumentId, int Ordinal);

/// <summary>A chunk a worker has claimed.</summary>
public sealed record SimAllChunk(Guid JobId, string StrategyId, int Version, string StrategyHash, Guid InstrumentId);

/// <summary>How a chunk ended: its stored run (done) or its reason (failed).</summary>
public sealed record SimAllChunkResult(string State, string? RunKey, int Trades, long RecordBytes, long Millis, string? FailCode, string? FailText);

/// <summary>The counts of a job's chunks, and the measures of the finished ones (the basis of the estimate).</summary>
public sealed record SimAllCounts(int Total, int Done, int Failed, int Running, int Pending, long SumMillis, long SumBytes, long SumTrades)
{
    /// <summary>Chunks not yet finished (pending or running).</summary>
    public int Remaining => Pending + Running;
}

/// <summary>A job with its counts.</summary>
public sealed record SimAllSnapshot(SimAllJob Job, SimAllCounts Counts);

/// <summary>The answer of an insert: the new job, or the job that is active already.</summary>
public sealed record SimAllInsert(bool Created, SimAllJob Job);

/// <summary>The sweep ledger rows, one per strategy version, and the chunks of the job to insert in one go.</summary>
public sealed record NewSimAllJob(SimAllJob Job, IReadOnlyList<NewSimAllChunk> Chunks);

/// <summary>One strategy version of a job: it gets one <c>training_sweep</c> ledger row.</summary>
public sealed record SimAllSweep(string StrategyId, int Version, string StrategyHash);

/// <summary>The finished chunks of a job: the run keys of the done ones and the number of chunks that did not finish.</summary>
public sealed record SimAllFinished(IReadOnlyList<string> RunKeys, int NotFinished);
