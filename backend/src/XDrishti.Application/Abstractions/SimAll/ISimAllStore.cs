namespace XDrishti.Application.Abstractions.SimAll;

/// <summary>
/// <c>research.sim_all_jobs</c> and <c>research.sim_all_chunks</c> behind a port (spec AU-12). The tables are the queue and the progress: a restart reads them and goes on. At most one job is active (running or paused), enforced by the database.
/// </summary>
public interface ISimAllStore
{
    /// <summary>Creates the job and all its chunks in one transaction, or answers the active job (nothing is written then).</summary>
    ValueTask<SimAllInsert> InsertAsync(NewSimAllJob job, CancellationToken cancellationToken);

    /// <summary>The active job, else the newest one, with its counts; null when there never was one.</summary>
    ValueTask<SimAllSnapshot?> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>The active job (running or paused) without counts, or null.</summary>
    ValueTask<SimAllJob?> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>Moves a running job, or a job paused by the system, to paused with the reason (the owner's pause takes over a system pause, so it does not lift itself); false otherwise.</summary>
    ValueTask<bool> PauseAsync(Guid id, string reason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Moves a paused job to running; when <paramref name="onlyReason"/> is given, only if it waits for that reason. False otherwise.</summary>
    ValueTask<bool> ResumeAsync(Guid id, string? onlyReason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Cancels an active job; false when it was final already.</summary>
    ValueTask<bool> CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Puts the chunks left running by a stopped host back to pending; returns how many.</summary>
    ValueTask<int> RedoRunningChunksAsync(CancellationToken cancellationToken);

    /// <summary>Claims the next pending chunk of a job that is running (<c>FOR UPDATE SKIP LOCKED</c>); null when none is left or the job is not running.</summary>
    ValueTask<SimAllChunk?> ClaimChunkAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Records how a claimed chunk ended.</summary>
    ValueTask FinishChunkAsync(SimAllChunk chunk, SimAllChunkResult result, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The distinct strategy versions of the job's chunks, for the sweep ledger rows.</summary>
    ValueTask<IReadOnlyList<SimAllSweep>> ListSweepsAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Ends a running job whose chunks are all finished, with its end state; null when chunks are left or the job is not running.</summary>
    ValueTask<string?> CompleteIfFinishedAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken);
}
