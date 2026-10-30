namespace XDrishti.Execution.Application.Halts;

/// <summary>An in-memory halt record for tests and the CI rollout step: rows are only ever added; an owner release is simulated by <see cref="Release"/> (the real one is written by xd-api, never from here).</summary>
public sealed class InMemoryHaltStore : IHaltStore
{
    private readonly Lock _gate = new();
    private readonly List<ActiveHalt> _halts = [];
    private readonly HashSet<long> _ended = [];
    private readonly List<(long HaltId, string Code)> _autoClears = [];

    /// <summary>Makes every read throw, as an unreadable database does.</summary>
    public bool Unreadable { get; set; }

    public IReadOnlyList<(long HaltId, string Code)> AutoClears
    {
        get
        {
            lock (_gate)
            {
                return [.. _autoClears];
            }
        }
    }

    public int Rows
    {
        get
        {
            lock (_gate)
            {
                return _halts.Count;
            }
        }
    }

    public Task<(ActiveHalt Halt, bool Created)> AppendAsync(HaltDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        lock (_gate)
        {
            ThrowIfUnreadable();
            if (HaltTriggers.Find(draft.TriggerCode) is null)
            {
                throw new ArgumentException("Unknown trigger code.", nameof(draft));
            }

            var same = _halts.FirstOrDefault(h => !_ended.Contains(h.Id) && h.TriggerCode == draft.TriggerCode && h.Scope == draft.Scope && h.AccountId == draft.AccountId && h.Level >= draft.Level);
            if (same is not null)
            {
                return Task.FromResult((same, false));
            }

            var halt = new ActiveHalt(_halts.Count + 1, draft.At, draft.Scope, draft.AccountId, draft.Level, draft.TriggerCode, draft.Source, draft.Actor, draft.Reason, draft.BrokerKillSwitch);
            _halts.Add(halt);
            return Task.FromResult((halt, true));
        }
    }

    public Task<IReadOnlyList<ActiveHalt>> ActiveAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ThrowIfUnreadable();
            return Task.FromResult<IReadOnlyList<ActiveHalt>>([.. _halts.Where(h => !_ended.Contains(h.Id))]);
        }
    }

    public Task<bool> AutoClearAsync(long haltId, string triggerCode, string detail, DateTimeOffset at, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ThrowIfUnreadable();
            var halt = _halts.FirstOrDefault(h => h.Id == haltId);
            if (halt is null || _ended.Contains(haltId) || halt.TriggerCode != triggerCode || !HaltTriggers.IsAutoClearing(triggerCode))
            {
                return Task.FromResult(false);
            }

            _ended.Add(haltId);
            _autoClears.Add((haltId, triggerCode));
            return Task.FromResult(true);
        }
    }

    /// <summary>Test stand-in for the owner's release written by xd-api.</summary>
    public void Release(long haltId)
    {
        lock (_gate)
        {
            _ended.Add(haltId);
        }
    }

    private void ThrowIfUnreadable()
    {
        if (Unreadable)
        {
            throw new InvalidOperationException("the halt record cannot be read");
        }
    }
}
