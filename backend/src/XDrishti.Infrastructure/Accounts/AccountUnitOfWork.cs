using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Accounts;

/// <summary>
/// The scoped context tracks the accounts, profiles, broker secrets and events of one use case; saving commits them in one transaction. A save here can re-date lots (the first import date), switch the Portfolio role or
/// "count in totals" or archive an account, which change what the portfolio histories show, so the cached histories are dropped after every commit (<see cref="IPortfolioHistoryCache"/>).
/// </summary>
internal sealed class AccountUnitOfWork(XDrishtiDbContext db, IPortfolioHistoryCache histories) : IAccountUnitOfWork
{
    /// <summary>Key of the transaction-scoped advisory lock that serialises <see cref="RunSerializedAsync"/> (switching the primary Data account, deleting a risk profile).</summary>
    internal const long SerializedWriteLockKey = 7_207_001L;

    // The one check a concurrent change can break: a sync closed a lot before the date it is being re-dated to. Any other check failure is a bug, not a race.
    private const string LotClosedAfterAcquired = "ck_holding_lots_closed_after_acquired";

    public async ValueTask<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            histories.Invalidate();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row version (xmin) of an account or profile changed since it was read: nothing was saved.
            db.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation } or PostgresException { SqlState: PostgresErrorCodes.CheckViolation, ConstraintName: LotClosedAfterAcquired })
        {
            // A uniqueness rule (primary/standby Data, slug, name), a reference (profile, bucket) or a check (a lot closed before its new first import date) was violated by a concurrent change.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async ValueTask<Result> RunSerializedAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken)
    {
        // The retrying execution strategy requires user transactions to run inside it; it may run the whole block again after a transient failure.
        var strategy = db.Database.CreateExecutionStrategy();
        var outcome = await strategy.ExecuteAsync(async token =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(token);

            // Held until the transaction ends: a second serialised write waits here and then reads what the first one committed.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SerializedWriteLockKey})", token);

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
        }, cancellationToken);
        if (outcome.IsSuccess)
        {
            histories.Invalidate();
        }

        return outcome;
    }
}
