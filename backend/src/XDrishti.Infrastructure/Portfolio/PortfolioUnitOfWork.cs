using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>The scoped context tracks the holdings and account events of one use case; saving commits them, a run commits them together with the sync record.</summary>
internal sealed class PortfolioUnitOfWork(XDrishtiDbContext db) : IPortfolioUnitOfWork
{
    public async ValueTask SaveAsync(CancellationToken cancellationToken) => await db.SaveChangesAsync(cancellationToken);

    public async ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
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
        return await strategy.ExecuteAsync(async token =>
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
        }, cancellationToken);
    }
}
