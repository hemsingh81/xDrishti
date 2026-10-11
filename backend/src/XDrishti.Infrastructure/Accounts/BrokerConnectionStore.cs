using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Accounts;

internal sealed class BrokerConnectionStore(XDrishtiDbContext db) : IBrokerConnectionStore
{
    public async ValueTask<BrokerConnection?> FindAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.BrokerConnections.FirstOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);

    public async ValueTask<IReadOnlyDictionary<Guid, BrokerConnection>> ListAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        if (accountIds.Count == 0)
        {
            return new Dictionary<Guid, BrokerConnection>();
        }

        return await db.BrokerConnections.AsNoTracking().Where(x => accountIds.Contains(x.AccountId)).ToDictionaryAsync(x => x.AccountId, cancellationToken);
    }

    public void Add(BrokerConnection connection) => db.BrokerConnections.Add(connection);
}
