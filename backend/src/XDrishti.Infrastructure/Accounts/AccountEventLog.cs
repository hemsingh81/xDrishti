using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Accounts;

internal sealed class AccountEventLog(XDrishtiDbContext db) : IAccountEventLog
{
    public void Record(AccountEvent accountEvent) => db.AccountEvents.Add(accountEvent);

    public async ValueTask<IReadOnlyList<AccountEvent>> ListForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken) =>
        await db.AccountEvents.AsNoTracking().Where(x => x.AccountId == accountId).OrderByDescending(x => x.At).ThenByDescending(x => x.Id).Take(take).ToListAsync(cancellationToken);
}
