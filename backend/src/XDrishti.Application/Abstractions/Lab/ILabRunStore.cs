using XDrishti.Domain.Lab;

namespace XDrishti.Application.Abstractions.Lab;

/// <summary>
/// Where Lab runs are kept and queued (spec P5-02, ADR 0058): <c>lab.lab_runs</c> behind a port. The table is the queue: a run is claimed by the host that runs it. The runtime role may insert a run and update only its state columns; it never deletes.
/// </summary>
public interface ILabRunStore
{
    /// <summary>
    /// Inserts the run, unless a queued, running or succeeded run has its key (D3): then that run is returned and nothing is written. Two posts racing for one key leave one row.
    /// </summary>
    ValueTask<InsertedLabRun> InsertOrGetAsync(NewLabRun run, CancellationToken cancellationToken);

    /// <summary>One run, or null.</summary>
    ValueTask<LabRun?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Runs newest first, optionally of one strategy and one state, at most <paramref name="limit"/>.</summary>
    ValueTask<IReadOnlyList<LabRun>> ListAsync(string? strategyId, LabRunState? state, int limit, CancellationToken cancellationToken);

    /// <summary>Takes the oldest queued run for <paramref name="host"/> and makes it running (stage <c>validate</c>), or null. Two hosts never get the same run.</summary>
    ValueTask<LabRun?> ClaimNextAsync(string host, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Records the stage reached and the progress of a running run; false when the run is not running any more.</summary>
    ValueTask<bool> ReportAsync(Guid id, string stage, int done, int total, IReadOnlyList<LabStageLog> stages, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Ends a running run; false when it was not running (nothing is changed).</summary>
    ValueTask<bool> FinishAsync(LabFinish finish, CancellationToken cancellationToken);

    /// <summary>Cancels a queued run at once, or asks a running one to stop at its next safe point.</summary>
    ValueTask<LabCancelOutcome> RequestCancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>True when the owner asked this run to stop.</summary>
    ValueTask<bool> IsCancelRequestedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Makes every run that <paramref name="host"/> had running <c>interrupted</c> (D4); returns their ids.</summary>
    ValueTask<IReadOnlyList<Guid>> InterruptRunningAsync(string host, DateTimeOffset now, CancellationToken cancellationToken);
}
