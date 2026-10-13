using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The scoped context tracks the holdings and account events of one use case; saving commits them, a run commits them together with the sync record. Whatever is saved here may change a lot, a holding or the benchmark setting, so the cached portfolio histories
/// are dropped after every save and every committed run (<see cref="IPortfolioHistoryCache"/>).
/// </summary>
internal sealed class PortfolioUnitOfWork(XDrishtiDbContext db, IPortfolioHistoryCache histories) : IPortfolioUnitOfWork
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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation or PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async ValueTask<Result> RunAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken)
    {
        // The retrying execution strategy requires user transactions to run inside it; it may run the whole block again after a transient failure.
        var strategy = db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async token =>
        {
            try
            {
                return await AttemptAsync(work, token);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A holding the work changed was changed by someone else meanwhile (a quote written between the work's read and its save, ADR 0019): the transaction is rolled back,
                // and the work runs once more from scratch, reading the rows as they are now (the work reads its own state). A second conflict is a failure of the run.
                return await AttemptAsync(work, token);
            }
        }, cancellationToken);
        if (result.IsSuccess)
        {
            histories.Invalidate();
        }

        return result;
    }

    private async Task<Result> AttemptAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        try
        {
            var result = await work(token);
            if (result.IsSuccess)
            {
                await transaction.CommitAsync(token);
            }
            else
            {
                await transaction.RollbackAsync(token);
                db.ChangeTracker.Clear();
            }

            return result;
        }
        catch
        {
            // Leaving the block rolls the transaction back; what was collected must not be saved by a later use of the context.
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
