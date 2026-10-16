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

    /// <summary>The reason code the health figures note for a run that was interrupted (see <c>SyncFailures.Interrupted</c>).</summary>
    private const string InterruptedReason = "interrupted";

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

        // A run that ran past its limit was interrupted: it is failed and the interruption is noted in the health figures (ADR 0027), in one statement.
        await using (var stale = new NpgsqlCommand(SyncHealthSql.FailRunning("portfolio.broker_sync_runs", "kind", "account_id = @account AND started_at < @cutoff"), connection, transaction))
        {
            stale.Parameters.AddWithValue("now", now.ToUniversalTime());
            stale.Parameters.AddWithValue("cutoff", (now - staleAfter).ToUniversalTime());
            stale.Parameters.AddWithValue("error", InterruptedMessage);
            stale.Parameters.AddWithValue("reason", InterruptedReason);
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

    public async ValueTask<DateOnly?> GetWatermarkAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken)
    {
        var health = Health(kind);
        return await db.BrokerSyncStates.AsNoTracking().Where(x => x.AccountId == accountId && x.Kind == health).Select(x => x.WatermarkDate).FirstOrDefaultAsync(cancellationToken);
    }

    private static SyncHealthKind Health(BrokerSyncKind kind) => kind == BrokerSyncKind.Trades ? SyncHealthKind.Trades : SyncHealthKind.Ledger;

    public async ValueTask MoveWatermarkAsync(Guid accountId, BrokerSyncKind kind, DateOnly from, DateOnly to, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var name = kind.ToString().ToLowerInvariant();
        var time = now.ToUniversalTime();
        // "Every date up to the watermark was read": it moves only when the window starts at or before the day after it. A first row (no watermark yet, or one that only holds health figures) is a first read from a chosen date.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO portfolio.broker_sync_state AS s (account_id, kind, watermark_date, updated_at)
            VALUES ({accountId}, {name}, {to}, {time})
            ON CONFLICT (account_id, kind) DO UPDATE
            SET updated_at = CASE WHEN s.watermark_date IS NULL OR EXCLUDED.watermark_date > s.watermark_date THEN EXCLUDED.updated_at ELSE s.updated_at END,
                watermark_date = GREATEST(s.watermark_date, EXCLUDED.watermark_date)
            WHERE s.watermark_date IS NULL OR s.watermark_date + 1 >= {from}
            """,
            cancellationToken);
    }

    public async ValueTask<BrokerSyncRun?> GetLastAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken) =>
        await db.BrokerSyncRuns.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Kind == kind)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async ValueTask<DateOnly?> GetEarliestReadAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken) =>
        await db.BrokerSyncRuns.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Kind == kind && x.Status == SyncStatus.Succeeded)
            .MinAsync(x => (DateOnly?)x.FromDate, cancellationToken);

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

    public async ValueTask<bool> IsRunningAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken) =>
        await db.BrokerSyncRuns.AsNoTracking().AnyAsync(x => x.AccountId == accountId && x.Kind == kind && x.Status == SyncStatus.Running, cancellationToken);

    public async ValueTask<IReadOnlyDictionary<Guid, BrokerSyncRun>> ListLastSucceededAsync(IReadOnlyCollection<Guid> accountIds, BrokerSyncKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        if (accountIds.Count == 0)
        {
            return new Dictionary<Guid, BrokerSyncRun>();
        }

        // The latest succeeded run of each account in one read: a run is the latest when no later succeeded run of the same account and kind exists.
        var latest = await db.BrokerSyncRuns.AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId) && x.Kind == kind && x.Status == SyncStatus.Succeeded
                && !db.BrokerSyncRuns.Any(y => y.AccountId == x.AccountId && y.Kind == kind && y.Status == SyncStatus.Succeeded && y.StartedAt > x.StartedAt))
            .ToListAsync(cancellationToken);
        return latest.GroupBy(x => x.AccountId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id).First());
    }

    public async ValueTask<IReadOnlyList<BrokerDateRange>> ListDroppedRangesAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken)
    {
        // The counts are JSON, read whole: a few hundred runs at most, and this is asked once per account and kind (the first version of ADR 0027).
        var runs = await db.BrokerSyncRuns.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Kind == kind && x.Status == SyncStatus.Succeeded)
            .Select(x => new { x.FromDate, x.ToDate, x.Counts })
            .ToListAsync(cancellationToken);
        return [.. runs
            .Where(r => r.Counts.Dropped > 0 || r.Counts.SkippedByReason.GetValueOrDefault(BrokerSkipReasons.NoSymbol) > 0)
            .Select(r => new BrokerDateRange(r.FromDate, r.ToDate))
            .OrderBy(r => r.From).ThenBy(r => r.To)];
    }

    public async ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(SyncHealthSql.FailRunning("portfolio.broker_sync_runs", "kind", "started_at < @started"));
        command.Parameters.AddWithValue("started", processStartedAt.ToUniversalTime());
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("error", InterruptedMessage);
        command.Parameters.AddWithValue("reason", InterruptedReason);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }
}
