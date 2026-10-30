using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Application.Recon;

/// <summary>The in-memory run and break record for tests and the CI rollout step: rows are only added; a resolution is added by <see cref="Resolve"/> the way xd-api would.</summary>
public sealed class InMemoryReconStore : IReconStore
{
    private readonly Lock _gate = new();
    private readonly List<(ReconRunRecord Run, DateOnly Session)> _runs = [];
    private readonly List<(long Id, long RunId, Guid Account, ReconBreakDraft Draft, DateTimeOffset At, DateOnly Session)> _breaks = [];
    private readonly Dictionary<long, ResolutionRecord> _resolutions = [];
    private long _runId;
    private long _breakId;

    public DateOnly Session { get; set; } = DateOnly.FromDateTime(DateTime.UnixEpoch);

    public Task<long> RecordRunAsync(Guid accountId, ReconKind kind, bool readable, int orders, int trades, int positions, int breakCount, IReadOnlyList<ReconBreakDraft> newBreaks, DateTimeOffset at, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var id = ++_runId;
            _runs.Add((new ReconRunRecord(id, at, accountId, kind, readable, breakCount), Session));
            foreach (var draft in newBreaks)
            {
                _breaks.Add((++_breakId, id, accountId, draft, at, Session));
            }

            return Task.FromResult(id);
        }
    }

    public Task<IReadOnlySet<string>> ResolvedKeysAsync(Guid accountId, DateOnly sessionDate, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlySet<string>>(_breaks.Where(b => b.Account == accountId && b.Session == sessionDate && _resolutions.ContainsKey(b.Id)).Select(b => b.Draft.Key).ToHashSet(StringComparer.Ordinal));
        }
    }

    public Task<IReadOnlySet<string>> OpenKeysAsync(Guid accountId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlySet<string>>(_breaks.Where(b => b.Account == accountId && !_resolutions.ContainsKey(b.Id)).Select(b => b.Draft.Key).ToHashSet(StringComparer.Ordinal));
        }
    }

    public Task<IReadOnlyList<StoredBreak>> BreaksAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<StoredBreak>>([.. _breaks.Where(b => accountId is null || b.Account == accountId).Select(b => new StoredBreak(b.Id, b.RunId, b.Account, b.Draft.Kind, b.Draft.Key, b.Draft.Symbol, b.Draft.IntentId, b.At, _resolutions.GetValueOrDefault(b.Id)))]);
        }
    }

    public Task<IReadOnlyList<ReconRunRecord>> RunsAsync(Guid accountId, DateOnly sessionDate, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ReconRunRecord>>([.. _runs.Where(r => r.Run.AccountId == accountId && r.Session == sessionDate).Select(r => r.Run)]);
        }
    }

    /// <summary>What xd-api's insert does: a resolution for an existing, unresolved break, by an owner actor; dismiss_foreign only for a foreign conflict. Returns false when the database would refuse.</summary>
    public bool Resolve(long breakId, string by, string resolution, DateTimeOffset at)
    {
        lock (_gate)
        {
            var row = _breaks.FirstOrDefault(b => b.Id == breakId);
            if (row.Id == 0 || _resolutions.ContainsKey(breakId) || !by.StartsWith("owner:", StringComparison.Ordinal) || by.Length <= 6
                || resolution is not ("accept_broker" or "dismiss_foreign") || (resolution == "dismiss_foreign" && row.Draft.Kind != BreakKind.ForeignConflict))
            {
                return false;
            }

            _resolutions[breakId] = new ResolutionRecord(at, by, resolution);
            return true;
        }
    }
}

/// <summary>A fixed picture of our books for tests; <see cref="Fingerprint"/> hashes it so a test can show a run changed nothing.</summary>
public sealed class StaticReconSource : IReconOurSource
{
    private readonly Dictionary<Guid, OurAccountView> _accounts = [];

    public void Set(Guid accountId, OurAccountView view) => _accounts[accountId] = view;

    public bool Fail { get; set; }

    public Task<IReadOnlyList<Guid>> AccountsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([.. _accounts.Keys]);

    public Task<OurAccountView> LoadAsync(Guid accountId, CancellationToken cancellationToken) =>
        Fail ? throw new InvalidOperationException("our record is unreadable") : Task.FromResult(_accounts.TryGetValue(accountId, out var v) ? v : new OurAccountView([], [], [], []));

    public string Fingerprint() =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', _accounts.OrderBy(a => a.Key).Select(a => $"{a.Key}|{string.Join(';', a.Value.Legs)}|{string.Join(';', a.Value.Fills)}|{string.Join(';', a.Value.Positions)}|{string.Join(';', a.Value.Intents)}")))));
}
