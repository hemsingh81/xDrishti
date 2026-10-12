using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>Records of data jobs. At most one job is <c>running</c> at any time, across all instances.</summary>
public interface IDataJobStore
{
    /// <summary>
    /// Registers <paramref name="job"/> as the running job unless another one is running (one older than <paramref name="staleAfter"/> counts as interrupted and
    /// is marked failed). False when another job is running. Serialised by a database advisory lock, backed by a unique index.
    /// </summary>
    ValueTask<bool> TryStartAsync(DataJob job, TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The job to work on, for reading its settings (changes are written by <see cref="PublishAsync"/>); null when it does not exist.</summary>
    ValueTask<DataJob?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the state of the job (phases, warnings, status, error) at once, visible to status requests while the job is busy. Only a job that is still
    /// <c>running</c> in the store is written: false (and nothing written) when it was ended elsewhere — failed as interrupted by the stale rule or by
    /// recovery — so a worker that is still busy cannot overwrite that verdict; the caller then stops reporting.
    /// </summary>
    ValueTask<bool> PublishAsync(DataJob job, CancellationToken cancellationToken);

    ValueTask<DataJob?> GetRunningAsync(CancellationToken cancellationToken);

    /// <summary>The latest job in which the phase ended with one of the <paramref name="statuses"/> (<see cref="PhaseStatus"/>), if any.</summary>
    ValueTask<DataJob?> GetLastWithPhaseAsync(string step, IReadOnlyCollection<string> statuses, CancellationToken cancellationToken);

    /// <summary>Marks every job still <c>running</c> that started before <paramref name="processStartedAt"/> as failed (interrupted); returns how many.</summary>
    ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<DataJob>> ListAsync(int take, CancellationToken cancellationToken);

    /// <summary>The latest job that was started by the trigger (finished, running or skipped); null when there is none.</summary>
    ValueTask<DataJob?> GetLastByTriggerAsync(DataJobTrigger trigger, CancellationToken cancellationToken);

    /// <summary>Records a run that did not start (<see cref="DataJob.Skipped"/>). It never counts as the running job.</summary>
    ValueTask RecordSkippedAsync(DataJob skipped, CancellationToken cancellationToken);
}
