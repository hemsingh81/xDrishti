using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Accounts;

/// <summary>
/// One in-memory stand-in for all account persistence ports (the vault included: it keeps the values unencrypted, as a database never
/// would); entities are shared by reference like a tracked DbContext.
/// </summary>
internal sealed class InMemoryAccountData : IAccountStore, IRiskProfileStore, IBucketStore, IAccountEventLog, IAccountUnitOfWork, IBrokerSecretVault
{
    private readonly List<Action> _pendingSecretUndo = [];

    private readonly List<BrokerAccount> _pendingAccounts = [];
    private readonly List<RiskProfile> _pendingProfiles = [];
    private readonly List<PortfolioBucket> _pendingBuckets = [];
    private readonly List<AccountEvent> _pendingEvents = [];
    private readonly List<RiskProfile> _pendingRemovals = [];

    public List<BrokerAccount> Accounts { get; } = [];

    public List<RiskProfile> Profiles { get; } = [];

    public List<PortfolioBucket> Buckets { get; } = [];

    public List<AccountEvent> Events { get; } = [];

    /// <summary>The vault's content: value and last change per account and secret name.</summary>
    public Dictionary<(Guid AccountId, BrokerSecretName Name), (SecretValue Value, DateTimeOffset UpdatedAt)> Secrets { get; } = [];

    public int SaveCount { get; private set; }

    public int SerializedRuns { get; private set; }

    /// <summary>Number of upcoming saves that report a collision with a concurrent change (everything collected is discarded).</summary>
    public int CollidingSaves { get; set; }

    public ValueTask<IReadOnlyList<BrokerAccount>> ListActiveAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<BrokerAccount>>([.. Accounts.Where(a => a.ArchivedAt is null).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)]);

    public ValueTask<BrokerAccount?> FindReadOnlyAsync(Guid id, CancellationToken cancellationToken) => FindAsync(id, cancellationToken);

    public ValueTask<BrokerAccount?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Accounts.FirstOrDefault(a => a.Id == id && a.ArchivedAt is null));

    public ValueTask<BrokerAccount?> FindPrimaryDataAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Accounts.FirstOrDefault(a => a.DataPrimary && a.ArchivedAt is null));

    public ValueTask<BrokerAccount?> FindStandbyDataAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Accounts.FirstOrDefault(a => a.DataStandby && a.ArchivedAt is null));

    public ValueTask<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Accounts.Any(a => a.Id == id));

    public ValueTask<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Accounts.Any(a => a.Slug == slug && a.ArchivedAt is null));

    public void Add(BrokerAccount account)
    {
        Accounts.Add(account);
        _pendingAccounts.Add(account);
    }

    ValueTask<IReadOnlyList<RiskProfile>> IRiskProfileStore.ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<RiskProfile>>([.. Profiles.OrderBy(p => p.Name)]);

    ValueTask<RiskProfile?> IRiskProfileStore.FindAsync(Guid id, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Profiles.FirstOrDefault(p => p.Id == id));

    ValueTask<string?> IRiskProfileStore.FindNameAsync(Guid id, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Profiles.FirstOrDefault(p => p.Id == id)?.Name);

    ValueTask<RiskProfile?> IRiskProfileStore.FindForDeleteAsync(Guid id, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Profiles.FirstOrDefault(p => p.Id == id));

    public ValueTask<bool> NameExistsAsync(string name, Guid? exceptProfileId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Id != exceptProfileId));

    public ValueTask<IReadOnlyDictionary<Guid, int>> CountUsageAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, int>>(Accounts
            .Where(a => a.ArchivedAt is null && a.Trading.RiskProfileId is not null)
            .GroupBy(a => a.Trading.RiskProfileId!.Value)
            .ToDictionary(g => g.Key, g => g.Count()));

    public void Add(RiskProfile profile)
    {
        Profiles.Add(profile);
        _pendingProfiles.Add(profile);
    }

    public void Remove(RiskProfile profile)
    {
        Profiles.Remove(profile);
        _pendingRemovals.Add(profile);
    }

    ValueTask<IReadOnlyList<PortfolioBucket>> IBucketStore.ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<PortfolioBucket>>([.. Buckets]);

    public ValueTask<string?> FindNameAsync(string name, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Buckets.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))?.Name);

    public void Add(PortfolioBucket bucket)
    {
        Buckets.Add(bucket);
        _pendingBuckets.Add(bucket);
    }

    public void Record(AccountEvent accountEvent)
    {
        Events.Add(accountEvent);
        _pendingEvents.Add(accountEvent);
    }

    public ValueTask<IReadOnlyList<AccountEvent>> ListForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AccountEvent>>([.. Events.Where(e => e.AccountId == accountId).OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Take(take)]);

    public ValueTask<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        if (CollidingSaves > 0)
        {
            CollidingSaves--;
            Accounts.RemoveAll(_pendingAccounts.Contains);
            Profiles.RemoveAll(_pendingProfiles.Contains);
            Buckets.RemoveAll(_pendingBuckets.Contains);
            Events.RemoveAll(_pendingEvents.Contains);
            Profiles.AddRange(_pendingRemovals);
            for (var i = _pendingSecretUndo.Count - 1; i >= 0; i--)
            {
                _pendingSecretUndo[i]();
            }

            ClearPending();
            return ValueTask.FromResult(false);
        }

        ClearPending();
        return ValueTask.FromResult(true);
    }

    public async ValueTask<Result> RunSerializedAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken)
    {
        SerializedRuns++;
        return await work(cancellationToken);
    }

    private void ClearPending()
    {
        _pendingAccounts.Clear();
        _pendingProfiles.Clear();
        _pendingBuckets.Clear();
        _pendingEvents.Clear();
        _pendingRemovals.Clear();
        _pendingSecretUndo.Clear();
    }

    public ValueTask SetAsync(Guid accountId, BrokerSecretName name, SecretValue value, DateTimeOffset now, CancellationToken cancellationToken)
    {
        RememberUndo((accountId, name));
        Secrets[(accountId, name)] = (value, now);
        return ValueTask.CompletedTask;
    }

    public ValueTask<SecretValue?> GetAsync(Guid accountId, BrokerSecretName name, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Secrets.TryGetValue((accountId, name), out var entry) ? entry.Value : null);

    public ValueTask DeleteAsync(Guid accountId, BrokerSecretName name, CancellationToken cancellationToken)
    {
        RememberUndo((accountId, name));
        Secrets.Remove((accountId, name));
        return ValueTask.CompletedTask;
    }

    ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<BrokerSecretInfo>>> IBrokerSecretVault.ListAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<BrokerSecretInfo>>>(Secrets
            .Where(s => accountIds.Contains(s.Key.AccountId))
            .GroupBy(s => s.Key.AccountId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BrokerSecretInfo>)[.. g.Select(s => new BrokerSecretInfo(s.Key.Name, s.Value.UpdatedAt))]));

    private void RememberUndo((Guid, BrokerSecretName) key)
    {
        if (Secrets.TryGetValue(key, out var previous))
        {
            _pendingSecretUndo.Add(() => Secrets[key] = previous);
        }
        else
        {
            _pendingSecretUndo.Add(() => Secrets.Remove(key));
        }
    }
}
