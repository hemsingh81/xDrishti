using System.Data.Common;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Infrastructure.Lab;

/// <summary>
/// <c>lab.lab_runs</c> behind <see cref="ILabRunStore"/> (spec P5-02, ADR 0058): raw SQL on the pooled data source. The table is the queue: a run is claimed with <c>FOR UPDATE SKIP LOCKED</c> in one statement. The runtime role inserts a run
/// and updates only the state columns (the migration grants by column); every update that moves a run is guarded by the state it expects, so a finished run never changes. JSON columns are written in the camelCase of the API. A strategy text, a trade or
/// a secret is never in a statement's text or a log line from here.
/// </summary>
internal sealed class LabRunStore(NpgsqlDataSource dataSource) : ILabRunStore
{
    private const string Columns = "id, key, strategy_id, version, strategy_hash, universe, window_from, window_to, method, config_hash, engine_version, state, stage, progress_done, progress_total, sim_run_keys, counts, problems, stages, labels, error, cancel_requested, created_at, started_at, finished_at";

    private const int MaxInsertAttempts = 3;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<InsertedLabRun> InsertOrGetAsync(NewLabRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        for (var attempt = 0; attempt < MaxInsertAttempts; attempt++)
        {
            await using var insert = dataSource.CreateCommand($"""
                INSERT INTO lab.lab_runs (id, key, strategy_id, version, strategy_hash, universe, window_from, window_to, method, config_hash, engine_version, state, progress_done, progress_total, sim_run_keys, problems, stages, labels, cancel_requested, created_at, updated_at)
                VALUES (@id, @key, @strategy_id, @version, @strategy_hash, @universe, @from, @to, @method, @config_hash, @engine_version, 'queued', 0, 0, ARRAY[]::text[], '[]', '[]', @labels, false, @now, @now)
                ON CONFLICT (key) WHERE state IN ('queued', 'running', 'succeeded') DO NOTHING
                RETURNING {Columns}
                """);
            insert.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, run.Id);
            insert.Parameters.AddWithValue("key", NpgsqlDbType.Char, run.Key);
            insert.Parameters.AddWithValue("strategy_id", NpgsqlDbType.Text, run.StrategyId);
            insert.Parameters.AddWithValue("version", NpgsqlDbType.Integer, run.Version);
            insert.Parameters.AddWithValue("strategy_hash", NpgsqlDbType.Char, run.StrategyHash);
            insert.Parameters.AddWithValue("universe", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(run.Universe, Json));
            insert.Parameters.AddWithValue("from", NpgsqlDbType.Date, run.From);
            insert.Parameters.AddWithValue("to", NpgsqlDbType.Date, run.To);
            insert.Parameters.AddWithValue("method", NpgsqlDbType.Text, run.Method);
            insert.Parameters.AddWithValue("config_hash", NpgsqlDbType.Char, run.ConfigHash);
            insert.Parameters.AddWithValue("engine_version", NpgsqlDbType.Text, run.EngineVersion);
            insert.Parameters.AddWithValue("labels", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(run.Labels, Json));
            insert.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, run.CreatedAt);
            await using (var reader = await insert.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    return new InsertedLabRun(Read(reader), true);
                }
            }

            // a run of the same key holds it: return that one (a failed run can free the key between the two statements, then the insert is tried again)
            await using var existing = dataSource.CreateCommand($"SELECT {Columns} FROM lab.lab_runs WHERE key = @key AND state IN ('queued', 'running', 'succeeded') ORDER BY created_at DESC LIMIT 1");
            existing.Parameters.AddWithValue("key", NpgsqlDbType.Char, run.Key);
            await using var found = await existing.ExecuteReaderAsync(cancellationToken);
            if (await found.ReadAsync(cancellationToken))
            {
                return new InsertedLabRun(Read(found), false);
            }
        }

        throw new InvalidOperationException("A Lab run could not be stored: its key kept changing hands.");
    }

    public async ValueTask<LabRun?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM lab.lab_runs WHERE id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlyList<LabRun>> ListAsync(string? strategyId, LabRunState? state, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {Columns} FROM lab.lab_runs
            WHERE (@strategy_id::text IS NULL OR strategy_id = @strategy_id) AND (@state::text IS NULL OR state = @state)
            ORDER BY created_at DESC, id DESC LIMIT @limit
            """);
        command.Parameters.AddWithValue("strategy_id", NpgsqlDbType.Text, (object?)strategyId ?? DBNull.Value);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Text, state is { } s ? LabRunStates.Code(s) : DBNull.Value);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<LabRun>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(Read(reader));
        }

        return list;
    }

    public async ValueTask<LabRun?> ClaimNextAsync(string host, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"""
            UPDATE lab.lab_runs SET state = 'running', stage = 'validate', host = @host, started_at = @now, updated_at = @now, progress_done = 0, progress_total = 0
            WHERE id = (SELECT id FROM lab.lab_runs WHERE state = 'queued' ORDER BY created_at, id FOR UPDATE SKIP LOCKED LIMIT 1)
            RETURNING {Columns}
            """);
        command.Parameters.AddWithValue("host", NpgsqlDbType.Text, host);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<bool> ReportAsync(Guid id, string stage, int done, int total, IReadOnlyList<LabStageLog> stages, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stages);
        await using var command = dataSource.CreateCommand("""
            UPDATE lab.lab_runs SET stage = @stage, progress_done = @done, progress_total = @total, stages = COALESCE(@stages, stages), updated_at = @now
            WHERE id = @id AND state = 'running'
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        command.Parameters.AddWithValue("stage", NpgsqlDbType.Text, stage);
        command.Parameters.AddWithValue("done", NpgsqlDbType.Integer, done);
        command.Parameters.AddWithValue("total", NpgsqlDbType.Integer, total);
        command.Parameters.AddWithValue("stages", NpgsqlDbType.Jsonb, stages.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(stages, Json));
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<bool> FinishAsync(LabFinish finish, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(finish);
        if (!LabRunStates.CanMove(LabRunState.Running, finish.State))
        {
            throw new ArgumentException("A running run ends succeeded, failed, cancelled or interrupted.", nameof(finish));
        }

        await using var command = dataSource.CreateCommand("""
            UPDATE lab.lab_runs SET state = @state, stage = @stage, sim_run_keys = @keys, counts = @counts, problems = @problems, stages = @stages, error = @error, finished_at = @at, updated_at = @at
            WHERE id = @id AND state = 'running'
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, finish.Id);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Text, LabRunStates.Code(finish.State));
        command.Parameters.AddWithValue("stage", NpgsqlDbType.Text, (object?)finish.Stage ?? DBNull.Value);
        command.Parameters.AddWithValue("keys", NpgsqlDbType.Array | NpgsqlDbType.Text, finish.SimRunKeys.ToArray());
        command.Parameters.AddWithValue("counts", NpgsqlDbType.Jsonb, finish.Counts is null ? DBNull.Value : JsonSerializer.Serialize(finish.Counts, Json));
        command.Parameters.AddWithValue("problems", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(finish.Problems, Json));
        command.Parameters.AddWithValue("stages", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(finish.Stages, Json));
        command.Parameters.AddWithValue("error", NpgsqlDbType.Text, (object?)finish.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, finish.FinishedAt);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<LabCancelOutcome> RequestCancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await NonQueryAsync("UPDATE lab.lab_runs SET state = 'cancelled', cancel_requested = true, finished_at = @now, updated_at = @now WHERE id = @id AND state = 'queued'", id, now, cancellationToken) == 1)
        {
            return LabCancelOutcome.Cancelled;
        }

        if (await NonQueryAsync("UPDATE lab.lab_runs SET cancel_requested = true, updated_at = @now WHERE id = @id AND state = 'running'", id, now, cancellationToken) == 1)
        {
            return LabCancelOutcome.Requested;
        }

        return await GetAsync(id, cancellationToken) is null ? LabCancelOutcome.NotFound : LabCancelOutcome.NotCancellable;
    }

    public async ValueTask<bool> IsCancelRequestedAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT cancel_requested FROM lab.lab_runs WHERE id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    public async ValueTask<IReadOnlyList<Guid>> InterruptRunningAsync(string host, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE lab.lab_runs SET state = 'interrupted', finished_at = @now, updated_at = @now WHERE state = 'running' AND host = @host RETURNING id");
        command.Parameters.AddWithValue("host", NpgsqlDbType.Text, host);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private async ValueTask<int> NonQueryAsync(string sql, Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static LabRun Read(DbDataReader r) => new(
        r.GetGuid(0),
        r.GetString(1),
        r.GetString(2),
        r.GetInt32(3),
        r.GetString(4),
        JsonSerializer.Deserialize<LabUniverse>(r.GetString(5), Json)!,
        r.GetFieldValue<DateOnly>(6),
        r.GetFieldValue<DateOnly>(7),
        r.GetString(8),
        r.GetString(9),
        r.GetString(10),
        LabRunStates.TryParse(r.GetString(11), out var state) ? state : throw new InvalidOperationException("A Lab run has a state this release does not know."),
        r.IsDBNull(12) ? null : r.GetString(12),
        r.GetInt32(13),
        r.GetInt32(14),
        r.GetFieldValue<string[]>(15),
        r.IsDBNull(16) ? null : JsonSerializer.Deserialize<LabCounts>(r.GetString(16), Json),
        JsonSerializer.Deserialize<List<LabProblem>>(r.GetString(17), Json)!,
        JsonSerializer.Deserialize<List<LabStageLog>>(r.GetString(18), Json)!,
        JsonSerializer.Deserialize<List<string>>(r.GetString(19), Json)!,
        r.IsDBNull(20) ? null : r.GetString(20),
        r.GetBoolean(21),
        r.GetFieldValue<DateTimeOffset>(22),
        r.IsDBNull(23) ? null : r.GetFieldValue<DateTimeOffset>(23),
        r.IsDBNull(24) ? null : r.GetFieldValue<DateTimeOffset>(24));
}
