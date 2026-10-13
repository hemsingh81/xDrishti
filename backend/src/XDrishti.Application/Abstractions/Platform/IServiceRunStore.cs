using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Abstractions.Platform;

/// <summary>The history of runs (<c>platform.service_runs</c>, never deleted from). At most one run of a service is <c>running</c>, across all instances.</summary>
public interface IServiceRunStore
{
    /// <summary>
    /// Records <paramref name="run"/> as the running run of its service; false (and nothing recorded) when the service has a running run already. A running run older than <paramref name="staleAfter"/>
    /// cannot be running (the longest timeout is shorter): it is closed as failed (interrupted) first, so a record that could not be closed does not block the service for good.
    /// </summary>
    ValueTask<bool> TryStartAsync(ServiceRun run, TimeSpan staleAfter, CancellationToken cancellationToken);

    /// <summary>Records a run that ended at once (skipped).</summary>
    ValueTask RecordAsync(ServiceRun ended, CancellationToken cancellationToken);

    /// <summary>Ends a running run. Nothing happens when it is not running (any more: closed by the recovery).</summary>
    ValueTask CompleteAsync(Guid runId, ServiceRunStatus status, string? message, DateTimeOffset finishedAt, CancellationToken cancellationToken);

    ValueTask LinkDataJobAsync(Guid runId, Guid dataJobId, CancellationToken cancellationToken);

    /// <summary>The latest run (by start, of any status) of each of the given services that has any: one indexed read per service, never a scan of the history.</summary>
    ValueTask<IReadOnlyDictionary<string, ServiceRun>> GetLatestAsync(IReadOnlyCollection<string> serviceIds, CancellationToken cancellationToken);

    /// <summary>The services that have a running run.</summary>
    ValueTask<IReadOnlySet<string>> GetRunningServicesAsync(CancellationToken cancellationToken);

    /// <summary>The runs of the service, newest first.</summary>
    ValueTask<IReadOnlyList<ServiceRun>> ListAsync(string serviceId, int skip, int take, CancellationToken cancellationToken);

    ValueTask<int> CountAsync(string serviceId, CancellationToken cancellationToken);

    /// <summary>Marks every run still <c>running</c> that started before <paramref name="processStartedAt"/> as failed (interrupted); returns how many.</summary>
    ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, string message, CancellationToken cancellationToken);
}
