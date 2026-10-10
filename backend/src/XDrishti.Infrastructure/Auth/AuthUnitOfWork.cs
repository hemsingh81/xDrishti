using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Auth;

/// <summary>The scoped context tracks the owner, sessions and audit entries of one use case; saving commits them in one transaction.</summary>
internal sealed class AuthUnitOfWork(XDrishtiDbContext db) : IAuthUnitOfWork
{
    public async ValueTask SaveChangesAsync(CancellationToken cancellationToken) =>
        await db.SaveChangesAsync(cancellationToken);

    public async ValueTask<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row version (xmin) of the owner changed: the update matched no row and the transaction rolled back.
            // Forget the stale entities so the next read comes from the database.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
