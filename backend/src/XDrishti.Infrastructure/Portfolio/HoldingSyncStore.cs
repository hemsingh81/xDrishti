using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Refresh;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

internal sealed class HoldingSyncStore(XDrishtiDbContext db, NpgsqlDataSource dataSource) : IHoldingSyncStore
{
    /// <summary>First key of the advisory lock that serialises starting a sync of one account (the second key is the account's hash).</summary>
    internal const int AdvisoryLockNamespace = 7_207_002;

    private const string InterruptedMessage = "The sync did not finish (the application stopped or crashed while it ran).";

    public async ValueTask<bool> TryStartAsync(HoldingSync run, TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@namespace, hashtext(@account))", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("namespace", AdvisoryLockNamespace);
            lockCommand.Parameters.AddWithValue("account", run.AccountId.ToString());
            if (await lockCommand.ExecuteScalarAsync(cancellationToken) is not true)
            {
                return false;
            }
        }

        await using (var stale = new NpgsqlCommand(
            """
            UPDATE portfolio.holding_syncs
            SET status = 'failed', finished_at = @now, error = @error
            WHERE account_id = @account AND status = 'running' AND started_at < @cutoff
            """, connection, transaction))
        {
            stale.Parameters.AddWithValue("now", now);
            stale.Parameters.AddWithValue("cutoff", now - staleAfter);
            stale.Parameters.AddWithValue("error", InterruptedMessage);
            stale.Parameters.AddWithValue("account", run.AccountId);
            await stale.ExecuteNonQueryAsync(cancellationToken);
        }

        // A trade or ledger read of the account (ADR 0025) takes the same turn: it holds the advisory lock while it starts, and a holdings sync does not start while one runs (one that ran past its limit counts as dead).
        await using (var running = new NpgsqlCommand(
            """
            SELECT EXISTS (SELECT 1 FROM portfolio.holding_syncs WHERE account_id = @account AND status = 'running')
                OR EXISTS (SELECT 1 FROM portfolio.broker_sync_runs WHERE account_id = @account AND status = 'running' AND started_at > @brokerCutoff)
            """, connection, transaction))
        {
            running.Parameters.AddWithValue("account", run.AccountId);
            running.Parameters.AddWithValue("brokerCutoff", now - SyncLimits.BrokerHistoryStaleAfter);
            if (await running.ExecuteScalarAsync(cancellationToken) is true)
            {
                return false;
            }
        }

        try
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO portfolio.holding_syncs
                    (id, account_id, "trigger", requested_by, started_at, status, new_count, increased_count, decreased_count, closed_count, unchanged_count)
                VALUES
                    (@id, @account, @trigger, @requestedBy, @startedAt, 'running', 0, 0, 0, 0, 0)
                """, connection, transaction);
            insert.Parameters.AddWithValue("id", run.Id);
            insert.Parameters.AddWithValue("account", run.AccountId);
            insert.Parameters.AddWithValue("trigger", run.Trigger.ToString().ToLowerInvariant());
            insert.Parameters.AddWithValue("requestedBy", run.RequestedBy);
            insert.Parameters.AddWithValue("startedAt", run.StartedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false; // the one-running-sync index caught a concurrent start
        }
    }

    public async ValueTask CompleteAsync(Guid id, SyncStatus status, SyncCounts counts, string? error, DateTimeOffset finishedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var text = error is { Length: > HoldingSync.MaxErrorLength } ? error[..HoldingSync.MaxErrorLength] : error;
        await db.HoldingSyncs.Where(x => x.Id == id && x.Status == SyncStatus.Running)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Status, status)
                    .SetProperty(x => x.FinishedAt, finishedAt)
                    .SetProperty(x => x.NewCount, counts.Added)
                    .SetProperty(x => x.IncreasedCount, counts.Increased)
                    .SetProperty(x => x.DecreasedCount, counts.Decreased)
                    .SetProperty(x => x.ClosedCount, counts.Closed)
                    .SetProperty(x => x.UnchangedCount, counts.Unchanged)
                    .SetProperty(x => x.Error, text),
                cancellationToken);
    }

    public async ValueTask DiscardAsync(Guid id, CancellationToken cancellationToken) =>
        await db.HoldingSyncs.Where(x => x.Id == id && x.Status == SyncStatus.Running).ExecuteDeleteAsync(cancellationToken);

    public async ValueTask DeleteOtherSkippedAsync(Guid accountId, DateTimeOffset from, DateTimeOffset to, Guid exceptId, CancellationToken cancellationToken) =>
        await db.HoldingSyncs.Where(x => x.AccountId == accountId && x.Id != exceptId && x.Status == SyncStatus.Skipped && x.Trigger == SyncTrigger.Auto && x.StartedAt >= from && x.StartedAt < to)
            .ExecuteDeleteAsync(cancellationToken);

    public async ValueTask RecordSkippedAsync(HoldingSync run, CancellationToken cancellationToken)
    {
        db.HoldingSyncs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<bool> HasSkippedBetweenAsync(Guid accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        await db.HoldingSyncs.AsNoTracking().AnyAsync(
            x => x.AccountId == accountId && x.Status == SyncStatus.Skipped && x.Trigger == SyncTrigger.Auto && x.StartedAt >= from && x.StartedAt < to,
            cancellationToken);

    public async ValueTask<int> PruneAsync(DateTimeOffset olderThan, int batch, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM portfolio.holding_syncs
            WHERE id IN (
                SELECT id FROM portfolio.holding_syncs
                WHERE started_at < {olderThan} AND status IN ('succeeded', 'skipped')
                ORDER BY started_at
                LIMIT {batch})
            """,
            cancellationToken);

    public async ValueTask<HoldingSync?> GetLastAsync(Guid? accountId, CancellationToken cancellationToken) =>
        await db.HoldingSyncs.AsNoTracking()
            .Where(x => accountId == null
                ? db.BrokerAccounts.Any(a => a.Id == x.AccountId && a.ArchivedAt == null && a.Portfolio.Enabled && a.Portfolio.IncludeInTotals)
                : x.AccountId == accountId)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async ValueTask<HoldingSync?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.HoldingSyncs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async ValueTask<bool> IsRunningAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.HoldingSyncs.AsNoTracking().AnyAsync(x => x.AccountId == accountId && x.Status == SyncStatus.Running, cancellationToken);

    public async ValueTask<IReadOnlyDictionary<Guid, SyncActivity>> ActivityAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        var ids = accountIds.ToArray();
        var automaticRequester = PortfolioRefreshRunner.Requester; // the first automatic sync of an account without holdings is an import, and still the day's attempt
        var rows = await db.HoldingSyncs.AsNoTracking()
            .Where(x => ids.Contains(x.AccountId))
            .GroupBy(x => x.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                LastAuto = g.Where(x => x.Trigger == SyncTrigger.Auto || (x.Trigger == SyncTrigger.Import && x.RequestedBy == automaticRequester))
                    .Max(x => (DateTimeOffset?)x.StartedAt),
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.AccountId, r => new SyncActivity(r.LastAuto));
    }

    public async ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.HoldingSyncs.Where(x => x.Status == SyncStatus.Running && x.StartedAt < processStartedAt)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Status, SyncStatus.Failed)
                    .SetProperty(x => x.FinishedAt, now)
                    .SetProperty(x => x.Error, InterruptedMessage),
                cancellationToken);
}
