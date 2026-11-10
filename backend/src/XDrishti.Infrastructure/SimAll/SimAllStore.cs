using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.SimAll;

namespace XDrishti.Infrastructure.SimAll;

/// <summary>
/// <c>research.sim_all_jobs</c> and <c>research.sim_all_chunks</c> behind <see cref="ISimAllStore"/> (spec AU-12): raw SQL. A chunk is claimed with <c>FOR UPDATE SKIP LOCKED</c> and only while its job is running, so a pause or a cancel stops the claims at once. The job insert takes an
/// advisory lock and writes the chunks in the same transaction, in one statement from arrays; the partial unique index on the active states is the backstop. Nothing here deletes.
/// </summary>
internal sealed class SimAllStore(NpgsqlDataSource dataSource) : ISimAllStore
{
    private const string JobColumns = "id, state, pause_reason, window_from, window_to, hold_out_start, workers, started_by, created_at, updated_at, finished_at";

    public async ValueTask<SimAllInsert> InsertAsync(NewSimAllJob newJob, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newJob);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('research.sim_all_jobs', 0))", connection, transaction))
        {
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        if (await ReadJobAsync(connection, transaction, $"SELECT {JobColumns} FROM research.sim_all_jobs WHERE state IN ('running', 'paused') LIMIT 1", cancellationToken) is { } active)
        {
            return new SimAllInsert(false, active);
        }

        var job = newJob.Job;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO research.sim_all_jobs (id, state, window_from, window_to, hold_out_start, workers, started_by, created_at, updated_at)
            VALUES (@id, 'running', @from, @to, @hold, @workers, @by, @at, @at)
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, job.Id);
            insert.Parameters.AddWithValue("from", NpgsqlDbType.Date, job.WindowFrom);
            insert.Parameters.AddWithValue("to", NpgsqlDbType.Date, job.WindowTo);
            insert.Parameters.AddWithValue("hold", NpgsqlDbType.Date, job.HoldOutStart);
            insert.Parameters.AddWithValue("workers", NpgsqlDbType.Integer, job.Workers);
            insert.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, job.StartedBy);
            insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, job.CreatedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var chunks = new NpgsqlCommand("""
            INSERT INTO research.sim_all_chunks (job_id, strategy_id, version, instrument_id, strategy_hash, ordinal)
            SELECT @job, s, v, i, h, o FROM unnest(@strategies, @versions, @instruments, @hashes, @ordinals) AS t(s, v, i, h, o)
            """, connection, transaction))
        {
            chunks.Parameters.AddWithValue("job", NpgsqlDbType.Uuid, job.Id);
            chunks.Parameters.AddWithValue("strategies", NpgsqlDbType.Array | NpgsqlDbType.Varchar, newJob.Chunks.Select(c => c.StrategyId).ToArray());
            chunks.Parameters.AddWithValue("versions", NpgsqlDbType.Array | NpgsqlDbType.Integer, newJob.Chunks.Select(c => c.Version).ToArray());
            chunks.Parameters.AddWithValue("instruments", NpgsqlDbType.Array | NpgsqlDbType.Uuid, newJob.Chunks.Select(c => c.InstrumentId).ToArray());
            chunks.Parameters.AddWithValue("hashes", NpgsqlDbType.Array | NpgsqlDbType.Char, newJob.Chunks.Select(c => c.StrategyHash).ToArray());
            chunks.Parameters.AddWithValue("ordinals", NpgsqlDbType.Array | NpgsqlDbType.Integer, newJob.Chunks.Select(c => c.Ordinal).ToArray());
            await chunks.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new SimAllInsert(true, job with { State = "running" });
    }

    public async ValueTask<SimAllSnapshot?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var job = await ReadJobAsync(connection, null, $"SELECT {JobColumns} FROM research.sim_all_jobs ORDER BY (state IN ('running', 'paused')) DESC, created_at DESC LIMIT 1", cancellationToken);
        if (job is null)
        {
            return null;
        }

        await using var command = new NpgsqlCommand("""
            SELECT count(*)::int,
                   count(*) FILTER (WHERE state = 'done')::int, count(*) FILTER (WHERE state = 'failed')::int,
                   count(*) FILTER (WHERE state = 'running')::int, count(*) FILTER (WHERE state = 'pending')::int,
                   coalesce(sum(millis) FILTER (WHERE state = 'done'), 0)::bigint, coalesce(sum(record_bytes) FILTER (WHERE state = 'done'), 0)::bigint, coalesce(sum(trades) FILTER (WHERE state = 'done'), 0)::bigint
            FROM research.sim_all_chunks WHERE job_id = @id
            """, connection);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, job.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new SimAllSnapshot(job, new SimAllCounts(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7)));
    }

    public async ValueTask<SimAllJob?> GetActiveAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadJobAsync(connection, null, $"SELECT {JobColumns} FROM research.sim_all_jobs WHERE state IN ('running', 'paused') LIMIT 1", cancellationToken);
    }

    public async ValueTask<SimAllJob?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT {JobColumns} FROM research.sim_all_jobs WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    public async ValueTask<SimAllFinished> ListFinishedAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT run_key, state FROM research.sim_all_chunks WHERE job_id = @job ORDER BY run_key NULLS LAST, ordinal");
        command.Parameters.AddWithValue("job", NpgsqlDbType.Uuid, jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var keys = new List<string>();
        var notFinished = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0) && reader.GetString(1) == "done")
            {
                keys.Add(reader.GetString(0));
            }
            else
            {
                notFinished++;
            }
        }

        return new SimAllFinished(keys, notFinished);
    }

    public ValueTask<bool> PauseAsync(Guid id, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        ChangeAsync("UPDATE research.sim_all_jobs SET state = 'paused', pause_reason = @reason, updated_at = @now WHERE id = @id AND (state = 'running' OR (state = 'paused' AND pause_reason <> 'owner' AND @reason = 'owner'))", id, reason, now, cancellationToken);

    public ValueTask<bool> ResumeAsync(Guid id, string? onlyReason, DateTimeOffset now, CancellationToken cancellationToken) =>
        ChangeAsync("UPDATE research.sim_all_jobs SET state = 'running', pause_reason = NULL, updated_at = @now WHERE id = @id AND state = 'paused' AND (@reason::text IS NULL OR pause_reason = @reason)", id, onlyReason, now, cancellationToken);

    public ValueTask<bool> CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
        ChangeAsync("UPDATE research.sim_all_jobs SET state = 'cancelled', pause_reason = NULL, updated_at = @now, finished_at = @now WHERE id = @id AND state IN ('running', 'paused')", id, null, now, cancellationToken);

    public async ValueTask<int> RedoRunningChunksAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE research.sim_all_chunks SET state = 'pending', started_at = NULL WHERE state = 'running'");
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<int> RedoStaleChunksAsync(DateTimeOffset startedBefore, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE research.sim_all_chunks SET state = 'pending', started_at = NULL WHERE state = 'running' AND started_at < @before");
        command.Parameters.AddWithValue("before", NpgsqlDbType.TimestampTz, startedBefore);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask ReleaseChunkAsync(SimAllChunk chunk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        await using var command = dataSource.CreateCommand("UPDATE research.sim_all_chunks SET state = 'pending', started_at = NULL WHERE job_id = @job AND strategy_id = @strategy AND version = @version AND instrument_id = @instrument AND state = 'running'");
        command.Parameters.AddWithValue("job", NpgsqlDbType.Uuid, chunk.JobId);
        command.Parameters.AddWithValue("strategy", NpgsqlDbType.Varchar, chunk.StrategyId);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, chunk.Version);
        command.Parameters.AddWithValue("instrument", NpgsqlDbType.Uuid, chunk.InstrumentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<SimAllChunk?> ClaimChunkAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE research.sim_all_chunks c SET state = 'running', started_at = @now
            WHERE (c.job_id, c.strategy_id, c.version, c.instrument_id) = (
                SELECT k.job_id, k.strategy_id, k.version, k.instrument_id FROM research.sim_all_chunks k
                WHERE k.job_id = @job AND k.state = 'pending' AND EXISTS (SELECT 1 FROM research.sim_all_jobs j WHERE j.id = @job AND j.state = 'running')
                ORDER BY k.ordinal LIMIT 1 FOR UPDATE OF k SKIP LOCKED)
            RETURNING c.strategy_id, c.version, c.strategy_hash, c.instrument_id
            """);
        command.Parameters.AddWithValue("job", NpgsqlDbType.Uuid, jobId);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new SimAllChunk(jobId, reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetGuid(3)) : null;
    }

    public async ValueTask FinishChunkAsync(SimAllChunk chunk, SimAllChunkResult result, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(result);
        await using var command = dataSource.CreateCommand("""
            UPDATE research.sim_all_chunks SET state = @state, run_key = @key, trades = @trades, record_bytes = @bytes, millis = @millis, fail_code = @code, fail_text = @text, finished_at = @now
            WHERE job_id = @job AND strategy_id = @strategy AND version = @version AND instrument_id = @instrument AND state = 'running'
            """);
        var p = command.Parameters;
        p.AddWithValue("state", NpgsqlDbType.Varchar, result.State);
        p.AddWithValue("key", NpgsqlDbType.Char, (object?)result.RunKey ?? DBNull.Value);
        p.AddWithValue("trades", NpgsqlDbType.Integer, result.Trades);
        p.AddWithValue("bytes", NpgsqlDbType.Bigint, result.RecordBytes);
        p.AddWithValue("millis", NpgsqlDbType.Bigint, result.Millis);
        p.AddWithValue("code", NpgsqlDbType.Varchar, (object?)result.FailCode ?? DBNull.Value);
        p.AddWithValue("text", NpgsqlDbType.Varchar, (object?)result.FailText ?? DBNull.Value);
        p.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        p.AddWithValue("job", NpgsqlDbType.Uuid, chunk.JobId);
        p.AddWithValue("strategy", NpgsqlDbType.Varchar, chunk.StrategyId);
        p.AddWithValue("version", NpgsqlDbType.Integer, chunk.Version);
        p.AddWithValue("instrument", NpgsqlDbType.Uuid, chunk.InstrumentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<SimAllSweep>> ListSweepsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT DISTINCT strategy_id, version, strategy_hash FROM research.sim_all_chunks WHERE job_id = @job ORDER BY strategy_id, version");
        command.Parameters.AddWithValue("job", NpgsqlDbType.Uuid, jobId);
        var list = new List<SimAllSweep>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SimAllSweep(reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));
        }

        return list;
    }

    public async ValueTask<string?> CompleteIfFinishedAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE research.sim_all_jobs j
            SET state = CASE WHEN EXISTS (SELECT 1 FROM research.sim_all_chunks c WHERE c.job_id = j.id AND c.state = 'failed') THEN 'completed_with_failures' ELSE 'completed' END,
                updated_at = @now, finished_at = @now
            WHERE j.id = @job AND j.state = 'running' AND NOT EXISTS (SELECT 1 FROM research.sim_all_chunks c WHERE c.job_id = j.id AND c.state IN ('pending', 'running'))
            RETURNING j.state
            """);
        command.Parameters.AddWithValue("job", NpgsqlDbType.Uuid, jobId);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private async ValueTask<bool> ChangeAsync(string sql, Guid id, string? reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        if (sql.Contains("@reason", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, (object?)reason ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async ValueTask<SimAllJob?> ReadJobAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    private static SimAllJob ReadJob(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetFieldValue<DateOnly>(3), reader.GetFieldValue<DateOnly>(4), reader.GetFieldValue<DateOnly>(5), reader.GetInt32(6), reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8), reader.GetFieldValue<DateTimeOffset>(9), reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10));
}
