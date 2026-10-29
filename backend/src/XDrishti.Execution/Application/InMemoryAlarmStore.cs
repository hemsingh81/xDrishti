using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Application;

/// <summary>An in-memory alarm record for tests and the CI rollout step: rows are only ever added.</summary>
public sealed class InMemoryAlarmStore : IAlarmStore
{
    private readonly Lock _gate = new();
    private readonly List<(long Id, AlarmDraft Draft)> _raised = [];
    private readonly List<(long AlarmId, string By, AlarmClearHow How, DateTimeOffset At)> _cleared = [];

    public IReadOnlyList<AlarmDraft> Raised
    {
        get
        {
            lock (_gate)
            {
                return [.. _raised.Select(r => r.Draft)];
            }
        }
    }

    public IReadOnlyList<AlarmDraft> Open
    {
        get
        {
            lock (_gate)
            {
                return [.. _raised.Where(r => _cleared.All(c => c.AlarmId != r.Id)).Select(r => r.Draft)];
            }
        }
    }

    public IReadOnlyList<AlarmClearHow> Clears
    {
        get
        {
            lock (_gate)
            {
                return [.. _cleared.Select(c => c.How)];
            }
        }
    }

    public Task<bool> RaiseAsync(AlarmDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        lock (_gate)
        {
            if (!Domain.Protection.AlarmCodes.All.ContainsKey(draft.Code))
            {
                throw new ArgumentException("Unknown alarm code.", nameof(draft));
            }

            // shortcut: de-dup is check-then-insert, safe for one watchdog (one writer) and not across two writers; upgrade: a partial unique index on open (code, account, intent) when a second writer exists.
            if (_raised.Any(r => r.Draft.Code == draft.Code && r.Draft.AccountId == draft.AccountId && r.Draft.IntentId == draft.IntentId && _cleared.All(c => c.AlarmId != r.Id)))
            {
                return Task.FromResult(false);
            }

            _raised.Add((_raised.Count + 1, draft));
            return Task.FromResult(true);
        }
    }

    public Task<int> ClearAsync(string code, Guid? accountId, long? intentId, string by, AlarmClearHow how, DateTimeOffset at, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var open = _raised.Where(r => r.Draft.Code == code && r.Draft.AccountId == accountId && r.Draft.IntentId == intentId && _cleared.All(c => c.AlarmId != r.Id)).ToList();
            foreach (var r in open)
            {
                _cleared.Add((r.Id, by, how, at));
            }

            return Task.FromResult(open.Count);
        }
    }
}
