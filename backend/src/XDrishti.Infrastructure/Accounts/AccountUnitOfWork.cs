using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Accounts;

/// <summary>The scoped context tracks the accounts, profiles, broker secrets and events of one use case; saving commits them in one transaction.</summary>
internal sealed class AccountUnitOfWork(XDrishtiDbContext db) : IAccountUnitOfWork
{
    /// <summary>Key of the transaction-scoped advisory lock that serialises <see cref="RunSerializedAsync"/> (switching the primary Data account, deleting a risk profile).</summary>
    internal const long SerializedWriteLockKey = 7_207_001L;

    public async ValueTask<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row version (xmin) of an account or profile changed since it was read: nothing was saved.
            db.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation })
        {
            // A uniqueness rule (primary/standby Data, slug, name) or a reference (profile, bucket) was violated by a concurrent change.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async ValueTask<Result> RunSerializedAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken)
    {
        // The retrying execution strategy requires user transactions to run inside it; it may run the whole block again after a transient failure.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async token =>
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
    }
}
