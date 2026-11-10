namespace XDrishti.Application.Reporting;

/// <summary>One job a training report can read: what it covers and how far it has got. A training simulation, not evidence.</summary>
/// <param name="Id">The job; pass it as <c>simJob</c>.</param>
/// <param name="State">running, paused, completed, completed_with_failures or cancelled.</param>
/// <param name="Complete">The job is completed with every chunk done and none failed.</param>
/// <param name="ChunksDone">Chunks finished.</param>
/// <param name="ChunksTotal">Chunks of the job.</param>
/// <param name="ChunksFailed">Chunks that failed.</param>
/// <param name="WindowFrom">First date of the training window.</param>
/// <param name="WindowTo">Last date of the training window.</param>
/// <param name="HoldOutStart">First date of the locked hold-out block.</param>
/// <param name="StartedAt">When the job was started.</param>
/// <param name="FinishedAt">When it ended; null while active.</param>
public sealed record SimReportJobView(Guid Id, string State, bool Complete, int ChunksDone, int ChunksTotal, int ChunksFailed, DateOnly WindowFrom, DateOnly WindowTo, DateOnly HoldOutStart, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt);

/// <summary>A page of jobs.</summary>
/// <param name="Label">Always the training-simulation label.</param>
/// <param name="Items">The jobs, newest first.</param>
/// <param name="NextCursor">The cursor of the next page; null on the last.</param>
public sealed record SimReportJobsView(string Label, IReadOnlyList<SimReportJobView> Items, string? NextCursor);
