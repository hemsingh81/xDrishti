using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Domain.Market.Quality;
using XDrishti.Infrastructure.Persistence.Configurations;

namespace XDrishti.Infrastructure.Market.Quality;

/// <summary>The quality runs and their results (<c>market.quality_runs</c>, <c>market.quality_results</c>). The runtime role may insert and update them, never delete.</summary>
internal sealed class QualityRunStore(NpgsqlDataSource dataSource) : IQualityRuns
{
    private const string InterruptedMessage = "The quality run did not finish (the application stopped or crashed while it ran).";

    public async ValueTask<bool> TryStartAsync(QualityRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // A run still marked running whose data job has ended (the job was failed by the 12-hour rule or by recovery) was interrupted: it must not block this one.
        await using (var orphans = new NpgsqlCommand(
            """
            UPDATE market.quality_runs q
            SET status = 'failed', finished_at = @now, error = @error
            WHERE q.status = 'running'
              AND NOT EXISTS (SELECT 1 FROM market.data_jobs j WHERE j.id = q.job_id AND j.status = 'running')
            """, connection, transaction))
        {
            orphans.Parameters.AddWithValue("now", run.StartedAt);
            orphans.Parameters.AddWithValue("error", InterruptedMessage);
            await orphans.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO market.quality_runs
                    (id, trigger, status, started_at, finished_at, sessions, instruments, passed, warned, failed, score, error, job_id, instrument_id)
                VALUES
                    (@id, @trigger, 'running', @startedAt, NULL, @sessions, @instruments, 0, 0, 0, NULL, NULL, @jobId, @instrumentId)
                """, connection, transaction);
            insert.Parameters.AddWithValue("id", run.Id);
            insert.Parameters.AddWithValue("trigger", run.Trigger.ToCode());
            insert.Parameters.AddWithValue("startedAt", run.StartedAt);
            insert.Parameters.AddWithValue("sessions", run.Sessions);
            insert.Parameters.AddWithValue("instruments", run.Instruments);
            insert.Parameters.AddWithValue("jobId", NpgsqlDbType.Uuid, (object?)run.JobId ?? DBNull.Value);
            insert.Parameters.AddWithValue("instrumentId", NpgsqlDbType.Uuid, (object?)run.InstrumentId ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false; // the one-running-run index caught a concurrent start
        }
    }

    public async ValueTask PublishAsync(QualityRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        // Only a run that is still running in the store is written: one that recovery failed meanwhile keeps that verdict.
        await using var command = dataSource.CreateCommand(
            """
            UPDATE market.quality_runs
            SET status = @status, finished_at = @finishedAt, passed = @passed, warned = @warned, failed = @failed, score = @score, error = @error
            WHERE id = @id AND status = 'running'
            """);
        command.Parameters.AddWithValue("id", run.Id);
        command.Parameters.AddWithValue("status", run.Status.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("finishedAt", NpgsqlDbType.TimestampTz, (object?)run.FinishedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("passed", run.Passed);
        command.Parameters.AddWithValue("warned", run.Warned);
        command.Parameters.AddWithValue("failed", run.Failed);
        command.Parameters.AddWithValue("score", NpgsqlDbType.Double, (object?)run.Score ?? DBNull.Value);
        command.Parameters.AddWithValue("error", NpgsqlDbType.Varchar, (object?)run.Error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask SaveResultsAsync(IReadOnlyCollection<QualityResult> results, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var result in results)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO market.quality_results (run_id, instrument_id, status, score, bars_checked, missing_minutes, checks)
                VALUES (@run, @instrument, @status, @score, @bars, @missing, @checks::jsonb)
                ON CONFLICT (run_id, instrument_id) DO UPDATE SET
                    status = EXCLUDED.status, score = EXCLUDED.score, bars_checked = EXCLUDED.bars_checked,
                    missing_minutes = EXCLUDED.missing_minutes, checks = EXCLUDED.checks
                """, connection, transaction);
            command.Parameters.AddWithValue("run", result.RunId);
            command.Parameters.AddWithValue("instrument", result.InstrumentId);
            command.Parameters.AddWithValue("status", result.Status);
            command.Parameters.AddWithValue("score", (short)result.Score);
            command.Parameters.AddWithValue("bars", result.BarsChecked);
            command.Parameters.AddWithValue("missing", result.MissingMinutes);
            command.Parameters.AddWithValue("checks", JsonSerializer.Serialize(result.Checks, DataJobConfiguration.Json));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE market.quality_runs
            SET status = 'failed', finished_at = @now, error = @error
            WHERE status = 'running' AND started_at < @processStartedAt
            """);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("processStartedAt", processStartedAt);
        command.Parameters.AddWithValue("error", InterruptedMessage);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
