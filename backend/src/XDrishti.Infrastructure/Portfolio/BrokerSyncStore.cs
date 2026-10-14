using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Infrastructure.Persistence.Configurations;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The runs and watermarks of the broker history sync (<c>portfolio.broker_sync_runs</c>, <c>portfolio.broker_sync_state</c>; ADR 0025). Starting a run takes the advisory lock that starts a holdings sync takes (<see cref="HoldingSyncStore.AdvisoryLockNamespace"/>),
/// so a holdings sync and a broker history run of one account never start together, whatever instance asks; a unique partial index backs the "one running run per account" rule. Progress, completion and the watermark are single statements that run in the surrounding
/// transaction (the window's unit of work). Nothing is deleted.
/// </summary>
internal sealed class BrokerSyncStore(XDrishtiDbContext db, NpgsqlDataSource dataSource) : IBrokerSyncStore
{
    private const string InterruptedMessage = "The sync did not finish (the application stopped or crashed while it ran).";

    public async ValueTask<bool> TryStartAsync(BrokerSyncRun run, TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@namespace, hashtext(@account))", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("namespace", HoldingSyncStore.AdvisoryLockNamespace);
            lockCommand.Parameters.AddWithValue("account", run.AccountId.ToString());
            if (await lockCommand.ExecuteScalarAsync(cancellationToken) is not true)
            {
                return false;
            }
        }

        await using (var stale = new NpgsqlCommand(
            """
            UPDATE portfolio.broker_sync_runs
            SET status = 'failed', finished_at = @now, error = @error
            WHERE account_id = @account AND status = 'running' AND started_at < @cutoff
            """, connection, transaction))
        {
            stale.Parameters.AddWithValue("now", now.ToUniversalTime());
            stale.Parameters.AddWithValue("cutoff", (now - staleAfter).ToUniversalTime());
            stale.Parameters.AddWithValue("error", InterruptedMessage);
            stale.Parameters.AddWithValue("account", run.AccountId);
            await stale.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var running = new NpgsqlCommand(
            """
            SELECT EXISTS (SELECT 1 FROM portfolio.broker_sync_runs WHERE account_id = @account AND status = 'running')
                OR EXISTS (SELECT 1 FROM portfolio.holding_syncs WHERE account_id = @account AND status = 'running' AND started_at > @holdingsCutoff)
            """, connection, transaction))
        {
            running.Parameters.AddWithValue("account", run.AccountId);
            running.Parameters.AddWithValue("holdingsCutoff", (now - SyncLimits.HoldingsStaleAfter).ToUniversalTime());
            if (await running.ExecuteScalarAsync(cancellationToken) is true)
            {
                return false;
            }
        }

        try
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO portfolio.broker_sync_runs
                    (id, account_id, kind, "trigger", from_date, to_date, status, counts, started_by, started_at)
                VALUES
                    (@id, @account, @kind, @trigger, @from, @to, 'running', @counts, @startedBy, @startedAt)
                """, connection, transaction);
            insert.Parameters.AddWithValue("id", run.Id);
            insert.Parameters.AddWithValue("account", run.AccountId);
            insert.Parameters.AddWithValue("kind", run.Kind.ToString().ToLowerInvariant());
            insert.Parameters.AddWithValue("trigger", run.Trigger.ToString().ToLowerInvariant());
            insert.Parameters.AddWithValue("from", run.FromDate);
            insert.Parameters.AddWithValue("to", run.ToDate);
            insert.Parameters.Add(new NpgsqlParameter("counts", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(run.Counts, DataJobConfiguration.Json) });
            insert.Parameters.AddWithValue("startedBy", run.StartedBy);
            insert.Parameters.AddWithValue("startedAt", run.StartedAt.ToUniversalTime());
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false; // the one-running-run index caught a concurrent start
        }
    }

    public async ValueTask RecordSkippedAsync(BrokerSyncRun run, CancellationToken cancellationToken)
    {
        db.BrokerSyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask UpdateProgressAsync(Guid runId, BrokerSyncCounts counts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);
        await db.BrokerSyncRuns.Where(x => x.Id == runId && x.Status == SyncStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Counts, counts), cancellationToken);
    }

    public async ValueTask CompleteAsync(Guid runId, SyncStatus status, BrokerSyncCounts counts, string? error, DateTimeOffset finishedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var text = error is { Length: > BrokerSyncRun.MaxErrorLength } ? error[..BrokerSyncRun.MaxErrorLength] : error;
        var time = finishedAt.ToUniversalTime();
        await db.BrokerSyncRuns.Where(x => x.Id == runId && x.Status == SyncStatus.Running)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Status, status)
                    .SetProperty(x => x.FinishedAt, time)
                    .SetProperty(x => x.Counts, counts)
                    .SetProperty(x => x.Error, text),
                cancellationToken);
    }

    public async ValueTask<DateOnly?> GetWatermarkAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken) =>
        await db.BrokerSyncStates.AsNoTracking().Where(x => x.AccountId == accountId && x.Kind == kind).Select(x => (DateOnly?)x.WatermarkDate).FirstOrDefaultAsync(cancellationToken);

    public async ValueTask MoveWatermarkAsync(Guid accountId, BrokerSyncKind kind, DateOnly from, DateOnly to, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var name = kind.ToString().ToLowerInvariant();
        var time = now.ToUniversalTime();
        // "Every date up to the watermark was read": it moves only when the window starts at or before the day after it. A first row (no watermark yet) is a first read from a chosen date.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO portfolio.broker_sync_state (account_id, kind, watermark_date, updated_at)
            VALUES ({accountId}, {name}, {to}, {time})
            ON CONFLICT (account_id, kind) DO UPDATE
            SET updated_at = CASE WHEN EXCLUDED.watermark_date > portfolio.broker_sync_state.watermark_date THEN EXCLUDED.updated_at ELSE portfolio.broker_sync_state.updated_at END,
                watermark_date = GREATEST(portfolio.broker_sync_state.watermark_date, EXCLUDED.watermark_date)
            WHERE portfolio.broker_sync_state.watermark_date + 1 >= {from}
            """,
            cancellationToken);
    }

    public async ValueTask<BrokerSyncRun?> GetLastAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken) =>
        await db.BrokerSyncRuns.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Kind == kind)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async ValueTask<BrokerSyncRun?> GetLastSucceededAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken) =>
        await db.BrokerSyncRuns.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Kind == kind && x.Status == SyncStatus.Succeeded)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async ValueTask<BrokerSyncRun?> GetPreviousFinishedAsync(Guid accountId, BrokerSyncKind kind, Guid excludedRunId, CancellationToken cancellationToken) =>
        await db.BrokerSyncRuns.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Kind == kind && x.Id != excludedRunId && (x.Status == SyncStatus.Succeeded || x.Status == SyncStatus.Failed))
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async ValueTask<BrokerSyncRun?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.BrokerSyncRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async ValueTask<bool> IsRunningAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.BrokerSyncRuns.AsNoTracking().AnyAsync(x => x.AccountId == accountId && x.Status == SyncStatus.Running, cancellationToken);

    public async ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var started = processStartedAt.ToUniversalTime();
        var finished = now.ToUniversalTime();
        return await db.BrokerSyncRuns.Where(x => x.Status == SyncStatus.Running && x.StartedAt < started)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Status, SyncStatus.Failed)
                    .SetProperty(x => x.FinishedAt, finished)
                    .SetProperty(x => x.Error, InterruptedMessage),
                cancellationToken);
    }
}
