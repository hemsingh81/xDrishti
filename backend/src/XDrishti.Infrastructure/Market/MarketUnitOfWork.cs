using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market;

/// <summary>The scoped context tracks everything one use case collected (tracking rows, baskets and their members, account events); saving commits it in one transaction.</summary>
internal sealed class MarketUnitOfWork(XDrishtiDbContext db) : IMarketUnitOfWork
{
    public async ValueTask SaveAsync(CancellationToken cancellationToken) => await db.SaveChangesAsync(cancellationToken);

    public async ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else changed a row this save was based on (for example a basket's revision).
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
