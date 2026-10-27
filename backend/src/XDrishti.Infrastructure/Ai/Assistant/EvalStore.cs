using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// <c>ai.eval_runs</c>, <c>ai.eval_cases</c> and <c>ai.eval_baselines</c> behind <see cref="IEvalStore"/> (spec P9-09 AC 14): raw SQL, insert and select only. A run and its cases are written in one transaction when the run ends; a baseline is written once per run.
/// </summary>
internal sealed class EvalStore(NpgsqlDataSource dataSource) : IEvalStore
{
    private const string RunColumns = "id, started_at, finished_at, target, profile, sets, repeats, gating, model_id, model_sha256, runtime_digest, params_json::text, prompt_versions_json::text, sets_hash, thresholds_hash, fixture_hash, fingerprint, status, verdict, summary_json::text, actor";

    private const string CaseColumns = "id, run_id, \"set\", case_id, repeat, passed, grounded, score, detail_json::text, answer, session_id, tool_calls, latency_ms, prompt_tokens, completion_tokens";

    public async ValueTask InsertRunAsync(NewEvalRun run, IReadOnlyList<NewEvalCase> cases, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(cases);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO ai.eval_runs (id, started_at, finished_at, target, profile, sets, repeats, gating, model_id, model_sha256, runtime_digest, params_json, prompt_versions_json, sets_hash, thresholds_hash, fixture_hash, fingerprint, status, verdict, summary_json, actor)
            VALUES (@id, @started, @finished, @target, @profile, @sets, @repeats, @gating, @model, @sha, @runtime, @params::jsonb, @prompts::jsonb, @setshash, @thresholds, @fixture, @fingerprint, @status, @verdict, @summary::jsonb, @actor)
            """, connection, transaction))
        {
            var p = insert.Parameters;
            p.AddWithValue("id", NpgsqlDbType.Uuid, run.Id);
            p.AddWithValue("started", NpgsqlDbType.TimestampTz, run.StartedAt.ToUniversalTime());
            p.AddWithValue("finished", NpgsqlDbType.TimestampTz, run.FinishedAt.ToUniversalTime());
            p.AddWithValue("target", NpgsqlDbType.Varchar, run.Target);
            p.AddWithValue("profile", NpgsqlDbType.Varchar, run.Profile);
            p.AddWithValue("sets", NpgsqlDbType.Array | NpgsqlDbType.Text, run.Sets.ToArray());
            p.AddWithValue("repeats", NpgsqlDbType.Integer, run.Repeats);
            p.AddWithValue("gating", NpgsqlDbType.Boolean, run.Gating);
            p.AddWithValue("model", NpgsqlDbType.Varchar, run.ModelId);
            p.AddWithValue("sha", NpgsqlDbType.Varchar, run.ModelSha256);
            p.AddWithValue("runtime", NpgsqlDbType.Varchar, run.RuntimeDigest);
            p.AddWithValue("params", NpgsqlDbType.Text, run.ParamsJson);
            p.AddWithValue("prompts", NpgsqlDbType.Text, run.PromptVersionsJson);
            p.AddWithValue("setshash", NpgsqlDbType.Char, run.SetsHash);
            p.AddWithValue("thresholds", NpgsqlDbType.Char, run.ThresholdsHash);
            p.AddWithValue("fixture", NpgsqlDbType.Char, run.FixtureHash);
            p.AddWithValue("fingerprint", NpgsqlDbType.Char, run.Fingerprint);
            p.AddWithValue("status", NpgsqlDbType.Varchar, run.Status);
            p.AddWithValue("verdict", NpgsqlDbType.Varchar, run.Verdict);
            p.AddWithValue("summary", NpgsqlDbType.Text, run.SummaryJson);
            p.AddWithValue("actor", NpgsqlDbType.Varchar, run.Actor);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var c in cases)
        {
            await using var insertCase = new NpgsqlCommand(
                """
                INSERT INTO ai.eval_cases (run_id, "set", case_id, repeat, passed, grounded, score, detail_json, answer, session_id, tool_calls, latency_ms, prompt_tokens, completion_tokens)
                VALUES (@run, @set, @case, @repeat, @passed, @grounded, @score, @detail::jsonb, @answer, @session, @calls, @latency, @prompt, @completion)
                """, connection, transaction);
            var p = insertCase.Parameters;
            p.AddWithValue("run", NpgsqlDbType.Uuid, run.Id);
            p.AddWithValue("set", NpgsqlDbType.Varchar, c.Set);
            p.AddWithValue("case", NpgsqlDbType.Varchar, c.CaseId);
            p.AddWithValue("repeat", NpgsqlDbType.Integer, c.Repeat);
            p.AddWithValue("passed", NpgsqlDbType.Boolean, c.Passed);
            p.AddWithValue("grounded", NpgsqlDbType.Boolean, (object?)c.Grounded ?? DBNull.Value);
            p.AddWithValue("score", NpgsqlDbType.Numeric, c.Score is { } score ? (object)Math.Round((decimal)score, 4) : DBNull.Value);
            p.AddWithValue("detail", NpgsqlDbType.Text, c.DetailJson);
            p.AddWithValue("answer", NpgsqlDbType.Text, c.Answer);
            p.AddWithValue("session", NpgsqlDbType.Uuid, (object?)c.SessionId ?? DBNull.Value);
            p.AddWithValue("calls", NpgsqlDbType.Integer, c.ToolCalls);
            p.AddWithValue("latency", NpgsqlDbType.Integer, c.LatencyMs);
            p.AddWithValue("prompt", NpgsqlDbType.Integer, (object?)c.PromptTokens ?? DBNull.Value);
            p.AddWithValue("completion", NpgsqlDbType.Integer, (object?)c.CompletionTokens ?? DBNull.Value);
            await insertCase.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<StoredEvalRun?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {RunColumns} FROM ai.eval_runs WHERE id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        var rows = await ReadRunsAsync(select, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<IReadOnlyList<StoredEvalRun>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {RunColumns} FROM ai.eval_runs ORDER BY finished_at DESC, id DESC LIMIT @limit");
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        return await ReadRunsAsync(select, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<StoredEvalCase>> CasesAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {CaseColumns} FROM ai.eval_cases WHERE run_id = @run ORDER BY \"set\", case_id, repeat");
        select.Parameters.AddWithValue("run", NpgsqlDbType.Uuid, runId);
        var list = new List<StoredEvalCase>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new StoredEvalCase(
                reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetBoolean(5), reader.IsDBNull(6) ? null : reader.GetBoolean(6), reader.IsDBNull(7) ? null : (double)reader.GetDecimal(7),
                reader.GetString(8), reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetGuid(10), reader.GetInt32(11), reader.GetInt32(12), reader.IsDBNull(13) ? null : reader.GetInt32(13), reader.IsDBNull(14) ? null : reader.GetInt32(14)));
        }

        return list;
    }

    public async ValueTask<StoredEvalRun?> NewestGatingAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {RunColumns} FROM ai.eval_runs WHERE gating ORDER BY finished_at DESC, id DESC LIMIT 1");
        var rows = await ReadRunsAsync(select, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<StoredEvalBaseline?> CurrentBaselineAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, run_id, accepted_by, reason, accepted_at FROM ai.eval_baselines ORDER BY id DESC LIMIT 1");
        return await ReadBaselineAsync(select, cancellationToken);
    }

    public async ValueTask<StoredEvalBaseline?> BaselineOfAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, run_id, accepted_by, reason, accepted_at FROM ai.eval_baselines WHERE run_id = @run");
        select.Parameters.AddWithValue("run", NpgsqlDbType.Uuid, runId);
        return await ReadBaselineAsync(select, cancellationToken);
    }

    public async ValueTask<(StoredEvalBaseline Baseline, bool Created)> InsertBaselineAsync(Guid runId, string acceptedBy, string? reason, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using (var insert = dataSource.CreateCommand("INSERT INTO ai.eval_baselines (run_id, accepted_by, reason, accepted_at) VALUES (@run, @by, @reason, @at) ON CONFLICT (run_id) DO NOTHING RETURNING id"))
        {
            insert.Parameters.AddWithValue("run", NpgsqlDbType.Uuid, runId);
            insert.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, acceptedBy);
            insert.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, (object?)reason ?? DBNull.Value);
            insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, at.ToUniversalTime());
            var created = await insert.ExecuteScalarAsync(cancellationToken) is long;
            var stored = await BaselineOfAsync(runId, cancellationToken) ?? throw new InvalidOperationException("The baseline row was not found after it was written.");
            return (stored, created);
        }
    }

    private static async Task<StoredEvalBaseline?> ReadBaselineAsync(NpgsqlCommand select, CancellationToken cancellationToken)
    {
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new StoredEvalBaseline(reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)) : null;
    }

    private static async Task<List<StoredEvalRun>> ReadRunsAsync(NpgsqlCommand select, CancellationToken cancellationToken)
    {
        var list = new List<StoredEvalRun>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new StoredEvalRun(
                reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<string[]>(5), reader.GetInt32(6), reader.GetBoolean(7),
                reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13), reader.GetString(14), reader.GetString(15), reader.GetString(16), reader.GetString(17),
                reader.GetString(18), reader.GetString(19), reader.GetString(20)));
        }

        return list;
    }
}
