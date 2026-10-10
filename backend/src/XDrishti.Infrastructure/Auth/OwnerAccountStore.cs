using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Domain.Auth;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Auth;

internal sealed class OwnerAccountStore(XDrishtiDbContext db) : IOwnerAccountStore
{
    public async ValueTask<OwnerAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        await db.OwnerAccounts.FirstOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);

    public async ValueTask<OwnerAccount?> FindSoleOwnerAsync(CancellationToken cancellationToken) =>
        await db.OwnerAccounts.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    public void Add(OwnerAccount owner) => db.OwnerAccounts.Add(owner);
}
