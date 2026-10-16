using System.Reflection;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>
/// The service rows, the run history, the audit trail and the unit of work in memory, behaving as the database does where it matters: a row gets a new revision on every save, a second running run of a
/// service is refused, a refused save drops what was collected, and a running run is closed only while it is running.
/// </summary>
internal sealed class InMemoryServiceData : IServiceScheduleStore, IServiceRunStore, IPlatformUnitOfWork, IAccountEventLog
{
    private static readonly PropertyInfo RevisionProperty = typeof(ServiceSchedule).GetProperty(nameof(ServiceSchedule.Revision))!;

    private readonly object _gate = new();
    private readonly List<ServiceSchedule> _pendingAdds = [];
    private readonly List<AccountEvent> _pendingEvents = [];
    private readonly HashSet<string> _tracked = new(StringComparer.Ordinal);
    private uint _revisions = 100;

    public Dictionary<string, ServiceSchedule> Rows { get; } = new(StringComparer.Ordinal);

    public List<ServiceRun> Runs { get; } = [];

    public List<AccountEvent> Events { get; } = [];

    public int Saves { get; private set; }

    /// <summary>The next saves that are refused, each one first running what a concurrent request would have done.</summary>
    public Queue<Action> Collisions { get; } = new();

    public IEnumerable<ServiceRun> RunsOf(string serviceId) => Runs.Where(r => r.ServiceId == serviceId).OrderBy(r => r.StartedAt).ThenBy(r => r.Attempt);

    /// <summary>A row as a save would have stored it (a new revision), without going through a handler.</summary>
    public ServiceSchedule Store(string serviceId, ServiceSettings settings, string by = "test")
    {
        lock (_gate)
        {
            if (Rows.TryGetValue(serviceId, out var row))
            {
                row.Apply(settings, by, DateTimeOffset.UnixEpoch);
            }
            else
            {
                row = ServiceSchedule.Create(serviceId, settings, by, DateTimeOffset.UnixEpoch);
                Rows[serviceId] = row;
            }

            RevisionProperty.SetValue(row, ++_revisions);
            return row;
        }
    }

    // ---- IServiceScheduleStore ----

    /// <summary>Runs when the rows are listed, before they are returned (a slow start-up: the clock moves meanwhile).</summary>
    public Action? OnList { get; set; }

    public ValueTask<IReadOnlyList<ServiceSchedule>> ListAsync(CancellationToken cancellationToken)
    {
        OnList?.Invoke();
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<ServiceSchedule>>([.. Rows.Values.OrderBy(r => r.Id, StringComparer.Ordinal).Select(Copy)]);
        }
    }

    /// <summary>Runs when a row is read for a change, before it is returned: what a concurrent request would have done meanwhile.</summary>
    public Action? OnFind { get; set; }

    public ValueTask<ServiceSchedule?> FindAsync(string serviceId, CancellationToken cancellationToken)
    {
        OnFind?.Invoke();
        lock (_gate)
        {
            if (Rows.TryGetValue(serviceId, out var row))
            {
                _tracked.Add(serviceId); // like a tracked entity: saved (and given a new revision) by the next save
            }

            return ValueTask.FromResult(row);
        }
    }

    public void Add(ServiceSchedule row)
    {
        lock (_gate)
        {
            _pendingAdds.Add(row);
        }
    }

    // ---- IAccountEventLog ----

    public void Record(AccountEvent accountEvent)
    {
        lock (_gate)
        {
            _pendingEvents.Add(accountEvent);
        }
    }

    public ValueTask<IReadOnlyList<AccountEvent>> ListForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AccountEvent>>([]);

    // ---- IPlatformUnitOfWork ----

    public ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Collisions.TryDequeue(out var concurrent))
            {
                _pendingAdds.Clear();
                _pendingEvents.Clear();
                _tracked.Clear();
                concurrent();
                return ValueTask.FromResult(false);
            }

            Saves++;
            foreach (var row in _pendingAdds)
            {
                Rows[row.Id] = row;
                _tracked.Add(row.Id);
            }

            // every row that was added or read for a change gets a new revision, like xmin
            foreach (var id in _tracked)
            {
                RevisionProperty.SetValue(Rows[id], ++_revisions);
            }

            _tracked.Clear();
            Events.AddRange(_pendingEvents);
            _pendingAdds.Clear();
            _pendingEvents.Clear();
            return ValueTask.FromResult(true);
        }
    }

    // ---- IServiceRunStore ----

    /// <summary>The token the latest start of a run was written under.</summary>
    public CancellationToken StartToken { get; private set; }

    /// <summary>When set, the next starts are refused as if the database had a running run of the service (another process).</summary>
    public bool RefuseStarts { get; set; }

    public ValueTask<bool> TryStartAsync(ServiceRun run, TimeSpan staleAfter, CancellationToken cancellationToken)
    {
        StartToken = cancellationToken;
        if (RefuseStarts)
        {
            return ValueTask.FromResult(false);
        }

        lock (_gate)
        {
            foreach (var stale in Runs.Where(r => r.ServiceId == run.ServiceId && r.IsRunning && r.StartedAt < run.StartedAt - staleAfter))
            {
                stale.Fail(run.StartedAt, "Interrupted: stale");
            }

            if (Runs.Any(r => r.ServiceId == run.ServiceId && r.IsRunning))
            {
                return ValueTask.FromResult(false);
            }

            Runs.Add(run);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask RecordAsync(ServiceRun ended, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Runs.Add(ended);
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask CompleteAsync(Guid runId, ServiceRunStatus status, string? message, DateTimeOffset finishedAt, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var run = Runs.FirstOrDefault(r => r.Id == runId && r.IsRunning);
            switch (status)
            {
                case ServiceRunStatus.Succeeded:
                    run?.Succeed(finishedAt, message);
                    break;
                case ServiceRunStatus.Failed:
                    run?.Fail(finishedAt, message ?? string.Empty);
                    break;
                default:
                    run?.Skip(finishedAt, message ?? string.Empty);
                    break;
            }

            return ValueTask.CompletedTask;
        }
    }

    public ValueTask LinkDataJobAsync(Guid runId, Guid dataJobId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Runs.FirstOrDefault(r => r.Id == runId)?.LinkDataJob(dataJobId);
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<IReadOnlyDictionary<string, ServiceRun>> GetLatestAsync(IReadOnlyCollection<string> serviceIds, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyDictionary<string, ServiceRun>>(
                Runs.Where(r => serviceIds.Contains(r.ServiceId)).GroupBy(r => r.ServiceId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Attempt).First(), StringComparer.Ordinal));
        }
    }

    public ValueTask<IReadOnlySet<string>> GetRunningServicesAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlySet<string>>(Runs.Where(r => r.IsRunning).Select(r => r.ServiceId).ToHashSet(StringComparer.Ordinal));
        }
    }

    public ValueTask<IReadOnlyList<ServiceRun>> ListAsync(string serviceId, int skip, int take, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<ServiceRun>>([.. Runs.Where(r => r.ServiceId == serviceId).OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Attempt).Skip(skip).Take(take)]);
        }
    }

    public ValueTask<int> CountAsync(string serviceId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(Runs.Count(r => r.ServiceId == serviceId));
        }
    }

    public ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, string message, IReadOnlyCollection<string> exceptServiceIds, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var running = Runs.Where(r => r.IsRunning && r.StartedAt < processStartedAt && !exceptServiceIds.Contains(r.ServiceId)).ToList();
            foreach (var run in running)
            {
                run.Fail(now, message);
            }

            return ValueTask.FromResult(running.Count);
        }
    }

    private static ServiceSchedule Copy(ServiceSchedule row)
    {
        var copy = ServiceSchedule.Create(row.Id, row.Settings, row.UpdatedBy, row.UpdatedAt);
        RevisionProperty.SetValue(copy, row.Revision);
        return copy;
    }
}
