using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary>
/// <c>research.experiment_jobs</c> behind <see cref="IExperimentJobStore"/> (spec P8-18 Part B, ADR 0129): raw SQL. The table is the queue: a job is claimed with <c>FOR UPDATE SKIP LOCKED</c>. An insert takes one advisory lock and, inside it, answers the identical input,
/// the one-at-a-time rule and the daily limit before it writes; the partial unique index on the active states is the backstop, and the insert trigger refuses a job while the capability is off. The runtime role updates only the state columns; nothing here removes a row.
/// </summary>
internal sealed class ExperimentJobStore(NpgsqlDataSource dataSource) : IExperimentJobStore
{
    private const string Columns = "id, job_key, baseline_run_key, knob_kind, knob_name, baseline_value, candidate_value, hold_out_months, reason, state, stage, progress_done, progress_total, cancel_requested, candidate_run_key, experiment_id, error_code, error_text, instruments, replay_ms, compare_ms, other_heavy_jobs_seen, created_by, created_at, started_at, finished_at, host, updated_at";

    public async ValueTask<ExperimentJobInsert> InsertAsync(NewExperimentJob job, int maxPerDay, TimeSpan window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('research.experiment_jobs', 0))", connection, transaction))
        {
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        if (await FindAsync(connection, transaction, $"SELECT {Columns} FROM research.experiment_jobs WHERE job_key = @key AND state IN ('queued', 'running', 'succeeded') ORDER BY created_at DESC LIMIT 1", ("key", job.JobKey), cancellationToken) is { } same)
        {
            return new ExperimentJobInsert(ExperimentJobInsertStatus.Existing, same, null);
        }

        if (await FindAsync(connection, transaction, $"SELECT {Columns} FROM research.experiment_jobs WHERE state IN ('queued', 'running') LIMIT 1", null, cancellationToken) is { } active)
        {
            return new ExperimentJobInsert(ExperimentJobInsertStatus.Active, active, null);
        }

        await using (var count = new NpgsqlCommand("SELECT count(*)::int, min(created_at) FROM research.experiment_jobs WHERE created_at >= @since", connection, transaction))
        {
            count.Parameters.AddWithValue("since", NpgsqlDbType.TimestampTz, job.At - window);
            await using var reader = await count.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            if (reader.GetInt32(0) >= maxPerDay && !reader.IsDBNull(1))
            {
                return new ExperimentJobInsert(ExperimentJobInsertStatus.DailyLimit, null, reader.GetFieldValue<DateTimeOffset>(1) + window);
            }
        }

        await using var insert = new NpgsqlCommand($"""
            INSERT INTO research.experiment_jobs (id, job_key, baseline_run_key, knob_kind, knob_name, baseline_value, candidate_value, hold_out_months, reason, state, instruments, created_by, created_at, updated_at)
            VALUES (@id, @key, @baseline, @kind, @name, @from, @to, @months, @reason, 'queued', @instruments, @by, @at, @at) RETURNING {Columns}
            """, connection, transaction);
        var p = insert.Parameters;
        p.AddWithValue("id", NpgsqlDbType.Uuid, job.Id);
        p.AddWithValue("key", NpgsqlDbType.Char, job.JobKey);
        p.AddWithValue("baseline", NpgsqlDbType.Char, job.BaselineRunKey);
        p.AddWithValue("kind", NpgsqlDbType.Varchar, job.KnobKind);
        p.AddWithValue("name", NpgsqlDbType.Varchar, job.KnobName);
        p.AddWithValue("from", NpgsqlDbType.Varchar, job.BaselineValue);
        p.AddWithValue("to", NpgsqlDbType.Varchar, job.CandidateValue);
        p.AddWithValue("months", NpgsqlDbType.Integer, job.HoldOutMonths);
        p.AddWithValue("reason", NpgsqlDbType.Varchar, job.Reason);
        p.AddWithValue("instruments", NpgsqlDbType.Integer, job.Instruments);
        p.AddWithValue("by", NpgsqlDbType.Varchar, job.CreatedBy);
        p.AddWithValue("at", NpgsqlDbType.TimestampTz, job.At);
        ExperimentJob created;
        try
        {
            await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            created = Read(reader);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            var other = await ActiveAsync(cancellationToken);
            return new ExperimentJobInsert(ExperimentJobInsertStatus.Active, other, null);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ExperimentJobInsert(ExperimentJobInsertStatus.Created, created, null);
    }

    public async ValueTask<ExperimentJob?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await QueryOneAsync($"SELECT {Columns} FROM research.experiment_jobs WHERE id = @id", [("id", id)], cancellationToken);

    public async ValueTask<IReadOnlyList<ExperimentJob>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM research.experiment_jobs ORDER BY created_at DESC, id DESC LIMIT @limit");
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 200));
        var rows = new List<ExperimentJob>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<int> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT count(*)::int FROM research.experiment_jobs WHERE created_at >= @since");
        command.Parameters.AddWithValue("since", NpgsqlDbType.TimestampTz, since);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask<ExperimentJob?> LatestMeasuredAsync(CancellationToken cancellationToken) =>
        await QueryOneAsync($"SELECT {Columns} FROM research.experiment_jobs WHERE state = 'succeeded' AND replay_ms IS NOT NULL ORDER BY finished_at DESC LIMIT 1", [], cancellationToken);

    public async ValueTask<ExperimentJob?> ActiveAsync(CancellationToken cancellationToken) =>
        await QueryOneAsync($"SELECT {Columns} FROM research.experiment_jobs WHERE state IN ('queued', 'running') LIMIT 1", [], cancellationToken);

    public async ValueTask<ExperimentJob?> ClaimNextAsync(string host, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"""
            UPDATE research.experiment_jobs SET state = 'running', stage = 'prepare', host = @host, started_at = @now, updated_at = @now, progress_done = 0, progress_total = instruments
            WHERE id = (SELECT id FROM research.experiment_jobs WHERE state = 'queued' ORDER BY created_at, id FOR UPDATE SKIP LOCKED LIMIT 1)
            RETURNING {Columns}
            """);
        command.Parameters.AddWithValue("host", NpgsqlDbType.Varchar, host);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<bool> ReportAsync(Guid id, string stage, int done, int total, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE research.experiment_jobs SET stage = @stage, progress_done = @done, progress_total = @total, updated_at = @now WHERE id = @id AND state = 'running'");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        command.Parameters.AddWithValue("stage", NpgsqlDbType.Varchar, stage);
        command.Parameters.AddWithValue("done", NpgsqlDbType.Integer, done);
        command.Parameters.AddWithValue("total", NpgsqlDbType.Integer, total);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask MarkOtherHeavyAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE research.experiment_jobs SET other_heavy_jobs_seen = true WHERE id = @id AND state = 'running'");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<bool> FinishAsync(ExperimentJobFinish finish, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(finish);
        if (!ExperimentJobStates.CanMove(ExperimentJobState.Running, finish.State))
        {
            throw new ArgumentException("A running job ends succeeded, failed, cancelled or interrupted.", nameof(finish));
        }

        await using var command = dataSource.CreateCommand("""
            UPDATE research.experiment_jobs SET state = @state, stage = @stage, candidate_run_key = @candidate, experiment_id = @experiment, error_code = @code, error_text = @text,
                replay_ms = @replay, compare_ms = @compare, other_heavy_jobs_seen = @heavy, finished_at = @at, updated_at = @at
            WHERE id = @id AND state = 'running'
            """);
        var p = command.Parameters;
        p.AddWithValue("id", NpgsqlDbType.Uuid, finish.Id);
        p.AddWithValue("state", NpgsqlDbType.Varchar, ExperimentJobStates.Code(finish.State));
        p.AddWithValue("stage", NpgsqlDbType.Varchar, (object?)finish.Stage ?? DBNull.Value);
        p.AddWithValue("candidate", NpgsqlDbType.Char, (object?)finish.CandidateRunKey ?? DBNull.Value);
        p.AddWithValue("experiment", NpgsqlDbType.Bigint, (object?)finish.ExperimentId ?? DBNull.Value);
        p.AddWithValue("code", NpgsqlDbType.Varchar, (object?)finish.ErrorCode ?? DBNull.Value);
        p.AddWithValue("text", NpgsqlDbType.Varchar, (object?)finish.ErrorText ?? DBNull.Value);
        p.AddWithValue("replay", NpgsqlDbType.Bigint, (object?)finish.ReplayMs ?? DBNull.Value);
        p.AddWithValue("compare", NpgsqlDbType.Bigint, (object?)finish.CompareMs ?? DBNull.Value);
        p.AddWithValue("heavy", NpgsqlDbType.Boolean, finish.OtherHeavyJobsSeen);
        p.AddWithValue("at", NpgsqlDbType.TimestampTz, finish.At);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<ExperimentJobCancelOutcome> RequestCancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var queued = new NpgsqlCommand("UPDATE research.experiment_jobs SET state = 'cancelled', finished_at = @now, updated_at = @now WHERE id = @id AND state = 'queued'", connection, transaction))
        {
            queued.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
            queued.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            if (await queued.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                await transaction.CommitAsync(cancellationToken);
                return ExperimentJobCancelOutcome.Cancelled;
            }
        }

        await using (var running = new NpgsqlCommand("UPDATE research.experiment_jobs SET cancel_requested = true, updated_at = @now WHERE id = @id AND state = 'running'", connection, transaction))
        {
            running.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
            running.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            if (await running.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                await transaction.CommitAsync(cancellationToken);
                return ExperimentJobCancelOutcome.Requested;
            }
        }

        await using var exists = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM research.experiment_jobs WHERE id = @id)", connection, transaction);
        exists.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        return (bool)(await exists.ExecuteScalarAsync(cancellationToken))! ? ExperimentJobCancelOutcome.NotCancellable : ExperimentJobCancelOutcome.NotFound;
    }

    public async ValueTask<bool> IsCancelRequestedAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT cancel_requested FROM research.experiment_jobs WHERE id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    public async ValueTask<IReadOnlyList<Guid>> InterruptRunningAsync(string host, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE research.experiment_jobs SET state = 'interrupted', finished_at = @now, updated_at = @now WHERE host = @host AND state = 'running' RETURNING id");
        command.Parameters.AddWithValue("host", NpgsqlDbType.Varchar, host);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private async ValueTask<ExperimentJob?> QueryOneAsync(string sql, (string Name, object Value)[] parameters, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static async ValueTask<ExperimentJob?> FindAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, (string Name, string Value)? parameter, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        if (parameter is { } p)
        {
            command.Parameters.AddWithValue(p.Name, NpgsqlDbType.Char, p.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static ExperimentJob Read(NpgsqlDataReader r) =>
        new(
            r.GetGuid(0),
            r.GetString(1),
            r.GetString(2),
            r.GetString(3),
            r.GetString(4),
            r.GetString(5),
            r.GetString(6),
            r.GetInt32(7),
            r.GetString(8),
            ExperimentJobStates.Parse(r.GetString(9)) ?? throw new InvalidOperationException("The table holds a job state this build does not know."),
            r.IsDBNull(10) ? null : r.GetString(10),
            r.GetInt32(11),
            r.GetInt32(12),
            r.GetBoolean(13),
            r.IsDBNull(14) ? null : r.GetString(14),
            r.IsDBNull(15) ? null : r.GetInt64(15),
            r.IsDBNull(16) ? null : r.GetString(16),
            r.IsDBNull(17) ? null : r.GetString(17),
            r.GetInt32(18),
            r.IsDBNull(19) ? null : r.GetInt64(19),
            r.IsDBNull(20) ? null : r.GetInt64(20),
            r.GetBoolean(21),
            r.GetString(22),
            r.GetFieldValue<DateTimeOffset>(23),
            r.IsDBNull(24) ? null : r.GetFieldValue<DateTimeOffset>(24),
            r.IsDBNull(25) ? null : r.GetFieldValue<DateTimeOffset>(25),
            r.IsDBNull(26) ? null : r.GetString(26),
            r.GetFieldValue<DateTimeOffset>(27));
}
