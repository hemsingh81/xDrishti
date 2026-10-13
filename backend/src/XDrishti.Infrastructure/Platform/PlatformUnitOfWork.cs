using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Platform;

/// <summary>The scoped context tracks what one use case collected (a service's row, the audit event); saving commits it in one transaction.</summary>
internal sealed class PlatformUnitOfWork(XDrishtiDbContext db) : IPlatformUnitOfWork
{
    public async ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // the row was changed (or the same save made twice) since it was read
            db.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
