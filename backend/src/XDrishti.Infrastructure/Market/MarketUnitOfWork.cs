using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market;

/// <summary>
/// The scoped context tracks everything one use case collected (tracking rows, baskets and their members, corporate actions, account events); saving commits it in one transaction. A corporate action changes the adjusted prices the portfolio histories are
/// built from, so the cached histories are dropped after every save (<see cref="IPortfolioHistoryCache"/>).
/// </summary>
internal sealed class MarketUnitOfWork(XDrishtiDbContext db, IPortfolioHistoryCache histories) : IMarketUnitOfWork
{
    public async ValueTask SaveAsync(CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        histories.Invalidate();
    }

    public async ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            histories.Invalidate();
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
