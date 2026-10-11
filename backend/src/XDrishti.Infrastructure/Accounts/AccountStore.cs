using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Accounts;

internal sealed class AccountStore(XDrishtiDbContext db) : IAccountStore
{
    public async ValueTask<IReadOnlyList<BrokerAccount>> ListActiveAsync(CancellationToken cancellationToken) =>
        await db.BrokerAccounts.AsNoTracking().Where(x => x.ArchivedAt == null).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);

    public async ValueTask<IReadOnlyDictionary<Guid, string>> ListPortfolioAccountNamesAsync(CancellationToken cancellationToken) =>
        await db.BrokerAccounts.AsNoTracking().Where(x => x.ArchivedAt == null && x.Portfolio.Enabled).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

    public async ValueTask<BrokerAccount?> FindReadOnlyAsync(Guid id, CancellationToken cancellationToken) =>
        await db.BrokerAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ArchivedAt == null, cancellationToken);

    public async ValueTask<BrokerAccount?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.BrokerAccounts.FirstOrDefaultAsync(x => x.Id == id && x.ArchivedAt == null, cancellationToken);

    public async ValueTask<BrokerAccount?> FindPrimaryDataAsync(CancellationToken cancellationToken) =>
        await db.BrokerAccounts.FirstOrDefaultAsync(x => x.DataPrimary && x.ArchivedAt == null, cancellationToken);

    public async ValueTask<BrokerAccount?> FindStandbyDataAsync(CancellationToken cancellationToken) =>
        await db.BrokerAccounts.FirstOrDefaultAsync(x => x.DataStandby && x.ArchivedAt == null, cancellationToken);

    public async ValueTask<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        await db.BrokerAccounts.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken);

    public async ValueTask<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        await db.BrokerAccounts.AsNoTracking().AnyAsync(x => x.Slug == slug && x.ArchivedAt == null, cancellationToken);

    public void Add(BrokerAccount account) => db.BrokerAccounts.Add(account);
}
