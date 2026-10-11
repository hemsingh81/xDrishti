using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Market;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market;

internal sealed class RefreshRunStore(XDrishtiDbContext db, NpgsqlDataSource dataSource) : IRefreshRunStore
{
    /// <summary>Key of the advisory lock that serialises starting a refresh across instances ("xdrishti" + "market").</summary>
    internal const long AdvisoryLockKey = 0x7864_6D61_726B_6574;

    private const string InterruptedMessage = "The refresh did not finish (the application stopped or crashed while it ran).";

    public async ValueTask<bool> TryStartAsync(RefreshRun run, TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@key)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
            if (await lockCommand.ExecuteScalarAsync(cancellationToken) is not true)
            {
                return false;
            }
        }

        await using (var stale = new NpgsqlCommand(
            """
            UPDATE market.master_refreshes
            SET status = 'failed', finished_at = @now, phase = NULL, error = @error
            WHERE status = 'running' AND started_at < @cutoff
            """, connection, transaction))
        {
            stale.Parameters.AddWithValue("now", now);
            stale.Parameters.AddWithValue("cutoff", now - staleAfter);
            stale.Parameters.AddWithValue("error", InterruptedMessage);
            await stale.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var running = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM market.master_refreshes WHERE status = 'running')", connection, transaction))
        {
            if (await running.ExecuteScalarAsync(cancellationToken) is true)
            {
                return false;
            }
        }

        try
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO market.master_refreshes
                    (id, "trigger", "force", requested_by, started_at, status, phase, rows_read, added, updated, retired, revived, unchanged,
                     skipped_by_scope, collisions, sector_matched, sector_unmatched, fno_underlyings, total_active, kept_by_type, warnings,
                     rules_version, sectors_applied)
                VALUES
                    (@id, @trigger, @force, @requestedBy, @startedAt, 'running', 'downloading', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '{}'::jsonb, '[]'::jsonb,
                     @rulesVersion, false)
                """, connection, transaction);
            insert.Parameters.AddWithValue("id", run.Id);
            insert.Parameters.AddWithValue("trigger", run.Trigger.ToString().ToLowerInvariant());
            insert.Parameters.AddWithValue("force", run.Force);
            insert.Parameters.AddWithValue("requestedBy", run.RequestedBy);
            insert.Parameters.AddWithValue("startedAt", run.StartedAt);
            insert.Parameters.AddWithValue("rulesVersion", run.RulesVersion);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false; // the one-running-refresh index caught a concurrent start
        }
    }

    public async ValueTask<RefreshRun?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.RefreshRuns.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async ValueTask SaveAsync(CancellationToken cancellationToken) => await db.SaveChangesAsync(cancellationToken);

    public async ValueTask ReportProgressAsync(Guid id, RefreshPhase phase, long rowsRead, CancellationToken cancellationToken) =>
        await db.RefreshRuns.Where(x => x.Id == id && x.Status == RefreshStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Phase, phase).SetProperty(x => x.RowsRead, rowsRead), cancellationToken);

    public async ValueTask<RefreshRun?> GetRunningAsync(CancellationToken cancellationToken) =>
        await db.RefreshRuns.AsNoTracking().Where(x => x.Status == RefreshStatus.Running).OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(cancellationToken);

    public async ValueTask<RefreshRun?> GetLastFinishedAsync(CancellationToken cancellationToken) =>
        await db.RefreshRuns.AsNoTracking().Where(x => x.Status != RefreshStatus.Running).OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(cancellationToken);

    public async ValueTask<(SourceStamp Dhan, SourceStamp Sector)?> GetLastStampsAsync(int rulesVersion, CancellationToken cancellationToken)
    {
        var last = await db.RefreshRuns.AsNoTracking()
            .Where(x => x.Status == RefreshStatus.Succeeded || x.Status == RefreshStatus.Unchanged)
            .OrderByDescending(x => x.StartedAt)
            .Select(x => new { x.DhanETag, x.DhanLastModified, x.SectorETag, x.SectorLastModified, x.RulesVersion, x.SectorsApplied })
            .FirstOrDefaultAsync(cancellationToken);
        return last is null || last.RulesVersion != rulesVersion || !last.SectorsApplied
            ? null
            : (new SourceStamp(last.DhanETag, last.DhanLastModified), new SourceStamp(last.SectorETag, last.SectorLastModified));
    }

    public async ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.RefreshRuns.Where(x => x.Status == RefreshStatus.Running && x.StartedAt < processStartedAt)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Status, RefreshStatus.Failed)
                    .SetProperty(x => x.FinishedAt, now)
                    .SetProperty(x => x.Phase, (RefreshPhase?)null)
                    .SetProperty(x => x.Error, InterruptedMessage),
                cancellationToken);

    public async ValueTask<IReadOnlyList<RefreshRun>> ListAsync(int take, CancellationToken cancellationToken) =>
        await db.RefreshRuns.AsNoTracking().OrderByDescending(x => x.StartedAt).Take(take).ToListAsync(cancellationToken);
}
