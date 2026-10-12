using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Infrastructure.Persistence.Configurations;

namespace XDrishti.Infrastructure.Market.Bars;

internal sealed class DataJobStore(XDrishtiDbContext db, NpgsqlDataSource dataSource) : IDataJobStore
{
    /// <summary>Key of the advisory lock that serialises starting a data job across instances ("xdrishti" + "data").</summary>
    internal const long AdvisoryLockKey = 0x7864_6461_7461_0001;

    private const string InterruptedMessage = "The update did not finish (the application stopped or crashed while it ran).";

    public async ValueTask<bool> TryStartAsync(DataJob job, TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
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
            UPDATE market.data_jobs
            SET status = 'failed', finished_at = @now, current_phase = NULL, error = @error
            WHERE status = 'running' AND started_at < @cutoff
            """, connection, transaction))
        {
            stale.Parameters.AddWithValue("now", now);
            stale.Parameters.AddWithValue("cutoff", now - staleAfter);
            stale.Parameters.AddWithValue("error", InterruptedMessage);
            await stale.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var running = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM market.data_jobs WHERE status = 'running')", connection, transaction))
        {
            if (await running.ExecuteScalarAsync(cancellationToken) is true)
            {
                return false;
            }
        }

        try
        {
            await InsertAsync(connection, transaction, job, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false; // the one-running-job index caught a concurrent start
        }
    }

    public async ValueTask RecordSkippedAsync(DataJob skipped, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skipped);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await InsertAsync(connection, transaction, skipped, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<DataJob?> GetLastByTriggerAsync(DataJobTrigger trigger, CancellationToken cancellationToken) =>
        await db.DataJobs.AsNoTracking().Where(x => x.Trigger == trigger).OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Inserts the job as it is: running for a started job (the one-running-job index applies), ended for a skipped one.</summary>
    private static async Task InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DataJob job, CancellationToken cancellationToken)
    {
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO market.data_jobs
                (id, requested_by, trigger, started_at, finished_at, status, steps, force_csv, refetch_gaps, rebuild_all, current_phase, csv, dhan, aggregate, quality, instrument_id, csv_files, undo_csv_file, warnings, error)
            VALUES
                (@id, @requestedBy, @trigger, @startedAt, @finishedAt, @status, @steps::jsonb, @forceCsv, @refetchGaps, @rebuildAll, NULL, @csv::jsonb, @dhan::jsonb, @aggregate::jsonb, @quality::jsonb, @instrumentId, @csvFiles::jsonb, @undoCsvFile, '[]'::jsonb, @error)
            """, connection, transaction);
        insert.Parameters.AddWithValue("id", job.Id);
        insert.Parameters.AddWithValue("requestedBy", job.RequestedBy);
        insert.Parameters.AddWithValue("trigger", job.Trigger.ToCode());
        insert.Parameters.AddWithValue("startedAt", job.StartedAt);
        insert.Parameters.AddWithValue("finishedAt", NpgsqlDbType.TimestampTz, (object?)job.FinishedAt ?? DBNull.Value);
        insert.Parameters.AddWithValue("status", job.Status.ToString().ToLowerInvariant());
        insert.Parameters.AddWithValue("steps", System.Text.Json.JsonSerializer.Serialize(job.Steps, DataJobConfiguration.Json));
        insert.Parameters.AddWithValue("forceCsv", job.ForceCsv);
        insert.Parameters.AddWithValue("refetchGaps", job.RefetchGaps);
        insert.Parameters.AddWithValue("rebuildAll", job.RebuildAll);
        insert.Parameters.AddWithValue("csv", System.Text.Json.JsonSerializer.Serialize(job.Csv, DataJobConfiguration.Json));
        insert.Parameters.AddWithValue("dhan", System.Text.Json.JsonSerializer.Serialize(job.Dhan, DataJobConfiguration.Json));
        insert.Parameters.AddWithValue("aggregate", System.Text.Json.JsonSerializer.Serialize(job.Aggregate, DataJobConfiguration.Json));
        insert.Parameters.AddWithValue("quality", System.Text.Json.JsonSerializer.Serialize(job.Quality, DataJobConfiguration.Json));
        insert.Parameters.AddWithValue("instrumentId", NpgsqlDbType.Uuid, (object?)job.InstrumentId ?? DBNull.Value);
        insert.Parameters.AddWithValue("csvFiles", NpgsqlDbType.Text, job.CsvFiles is null ? DBNull.Value : System.Text.Json.JsonSerializer.Serialize(job.CsvFiles, DataJobConfiguration.Json));
        insert.Parameters.AddWithValue("undoCsvFile", NpgsqlDbType.Varchar, (object?)job.UndoCsvFile ?? DBNull.Value);
        insert.Parameters.AddWithValue("error", NpgsqlDbType.Varchar, (object?)job.Error ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<DataJob?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.DataJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async ValueTask<bool> PublishAsync(DataJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var status = job.Status;
        var finishedAt = job.FinishedAt;
        var currentPhase = job.CurrentPhase;
        var csv = job.Csv;
        var dhan = job.Dhan;
        var aggregate = job.Aggregate;
        var quality = job.Quality;
        var warnings = job.Warnings;
        var error = job.Error;

        // Only a job that is still running is written: one that was failed meanwhile (the 12-hour rule of TryStartAsync, or recovery at start-up) keeps that verdict, and
        // zero rows updated tells the worker to stop reporting.
        var updated = await db.DataJobs.Where(x => x.Id == job.Id && x.Status == DataJobStatus.Running)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Status, status)
                    .SetProperty(x => x.FinishedAt, finishedAt)
                    .SetProperty(x => x.CurrentPhase, currentPhase)
                    .SetProperty(x => x.Csv, csv)
                    .SetProperty(x => x.Dhan, dhan)
                    .SetProperty(x => x.Aggregate, aggregate)
                    .SetProperty(x => x.Quality, quality)
                    .SetProperty(x => x.Warnings, warnings)
                    .SetProperty(x => x.Error, error),
                cancellationToken);
        return updated > 0;
    }

    public async ValueTask<DataJob?> GetRunningAsync(CancellationToken cancellationToken) =>
        await db.DataJobs.AsNoTracking().Where(x => x.Status == DataJobStatus.Running).OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(cancellationToken);

    public async ValueTask<DataJob?> GetLastWithPhaseAsync(string step, IReadOnlyCollection<string> statuses, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        var column = step switch
        {
            DataSteps.Csv => "csv",
            DataSteps.Dhan => "dhan",
            DataSteps.Aggregate => "aggregate",
            DataSteps.Quality => "quality",
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };

        // The column name comes from the constants above; the statuses are parameters. A job about one instrument (a fix action) is not the last run of a phase.
#pragma warning disable EF1002 // not user input: one of the constant column names
        return await db.DataJobs.FromSqlRaw($"SELECT * FROM market.data_jobs WHERE instrument_id IS NULL AND {column} ->> 'status' = ANY ({{0}}) ORDER BY started_at DESC LIMIT 1", (object)statuses.ToArray())
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
#pragma warning restore EF1002
    }

    public async ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var interrupted = await db.DataJobs.AsNoTracking().Where(x => x.Status == DataJobStatus.Running && x.StartedAt < processStartedAt).ToListAsync(cancellationToken);
        foreach (var job in interrupted)
        {
            job.Fail(now, InterruptedMessage);
            await PublishAsync(job, cancellationToken);
        }

        return interrupted.Count;
    }

    public async ValueTask<IReadOnlyList<DataJob>> ListAsync(int take, CancellationToken cancellationToken) =>
        await db.DataJobs.AsNoTracking().OrderByDescending(x => x.StartedAt).Take(take).ToListAsync(cancellationToken);
}
