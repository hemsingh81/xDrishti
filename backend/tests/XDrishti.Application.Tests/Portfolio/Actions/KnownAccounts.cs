using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>The account store as the Action-center read uses it: it only asks whether an account exists. Everything else is not part of the test.</summary>
internal sealed class KnownAccounts(IReadOnlySet<Guid> ids, BrokerAccount account) : IAccountStore
{
    public ValueTask<BrokerAccount?> FindReadOnlyAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(ids.Contains(id) ? account : null);

    public ValueTask<IReadOnlyList<BrokerAccount>> ListActiveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<IReadOnlyDictionary<Guid, string>> ListPortfolioAccountNamesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<BrokerAccount?> FindAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<BrokerAccount?> FindPrimaryDataAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<BrokerAccount?> FindStandbyDataAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

    public void Add(BrokerAccount account) => throw new NotSupportedException();
}
