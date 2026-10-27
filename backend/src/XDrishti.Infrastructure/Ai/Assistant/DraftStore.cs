using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Assistant.Drafting;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// <c>ai.strategy_drafts</c> and <c>ai.strategy_draft_decisions</c> behind <see cref="IDraftStore"/> (spec P9-07 AC 11): raw SQL, insert and select only. The draft row is written once, when its job ends, with the id reserved when it started. A draft has at most one decision (unique).
/// </summary>
internal sealed class DraftStore(NpgsqlDataSource dataSource) : IDraftStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Columns = "id, idea, status, fail_reason, via, basket_id, yaml, answer_json::text, attempts_json::text, problems_json::text, findings_json::text, model_id, prompt_version, sheet_hash, requested_by, created_at";

    public async ValueTask<long> ReserveIdAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT nextval(pg_get_serial_sequence('ai.strategy_drafts', 'id'))");
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask InsertAsync(NewDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO ai.strategy_drafts (id, idea, status, fail_reason, via, basket_id, yaml, answer_json, attempts_json, problems_json, findings_json, model_id, model_sha256, prompt_version, sheet_hash, params_json, requested_by)
            VALUES (@id, @idea, @status, @reason, @via, @basket, @yaml, @answer::jsonb, @attempts::jsonb, @problems::jsonb, @findings::jsonb, @model, @sha, @prompt, @sheet, @params::jsonb, @by)
            """);
        var p = insert.Parameters;
        p.AddWithValue("id", NpgsqlDbType.Bigint, draft.Id);
        p.AddWithValue("idea", NpgsqlDbType.Text, draft.Idea);
        p.AddWithValue("status", NpgsqlDbType.Varchar, draft.Status);
        p.AddWithValue("reason", NpgsqlDbType.Varchar, (object?)draft.FailReason ?? DBNull.Value);
        p.AddWithValue("via", NpgsqlDbType.Varchar, draft.Via);
        p.AddWithValue("basket", NpgsqlDbType.Uuid, (object?)draft.BasketId ?? DBNull.Value);
        p.AddWithValue("yaml", NpgsqlDbType.Text, (object?)draft.Yaml ?? DBNull.Value);
        p.AddWithValue("answer", NpgsqlDbType.Text, JsonSerializer.Serialize(draft.Answer, Json));
        p.AddWithValue("attempts", NpgsqlDbType.Text, JsonSerializer.Serialize(draft.Attempts, Json));
        p.AddWithValue("problems", NpgsqlDbType.Text, JsonSerializer.Serialize(draft.Problems, Json));
        p.AddWithValue("findings", NpgsqlDbType.Text, JsonSerializer.Serialize(draft.Findings, Json));
        p.AddWithValue("model", NpgsqlDbType.Varchar, (object?)draft.ModelId ?? DBNull.Value);
        p.AddWithValue("sha", NpgsqlDbType.Char, (object?)draft.ModelSha256 ?? DBNull.Value);
        p.AddWithValue("prompt", NpgsqlDbType.Varchar, draft.PromptVersion);
        p.AddWithValue("sheet", NpgsqlDbType.Char, draft.SheetHash);
        p.AddWithValue("params", NpgsqlDbType.Text, draft.ParamsJson);
        p.AddWithValue("by", NpgsqlDbType.Varchar, draft.RequestedBy);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<StoredDraft?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM ai.strategy_drafts WHERE id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        var rows = await ReadAsync(select, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<IReadOnlyList<StoredDraft>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM ai.strategy_drafts ORDER BY id DESC LIMIT @limit");
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        return await ReadAsync(select, cancellationToken);
    }

    public async ValueTask<int> CountBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT count(*)::int FROM ai.strategy_drafts WHERE created_at >= @from AND created_at < @to");
        select.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, from.ToUniversalTime());
        select.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, to.ToUniversalTime());
        return (int)(await select.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask<StoredDraftDecision?> DecisionAsync(long draftId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT draft_id, decision, strategy_id, strategy_version, lab_run_id, edited, auto, actor, decided_at FROM ai.strategy_draft_decisions WHERE draft_id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, draftId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new StoredDraftDecision(
                reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetBoolean(5), reader.GetBoolean(6), reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8))
            : null;
    }

    public async ValueTask<(StoredDraftDecision Decision, bool Created)> InsertDecisionAsync(NewDraftDecision decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO ai.strategy_draft_decisions (draft_id, decision, strategy_id, strategy_version, lab_run_id, edited, auto, settings_id, snapshot_hash, actor)
            VALUES (@draft, @decision, @strategy, @version, @run, @edited, @auto, @settings, @hash, @actor)
            ON CONFLICT (draft_id) DO NOTHING
            """);
        var p = insert.Parameters;
        p.AddWithValue("draft", NpgsqlDbType.Bigint, decision.DraftId);
        p.AddWithValue("decision", NpgsqlDbType.Varchar, decision.Decision);
        p.AddWithValue("strategy", NpgsqlDbType.Varchar, (object?)decision.StrategyId ?? DBNull.Value);
        p.AddWithValue("version", NpgsqlDbType.Integer, (object?)decision.StrategyVersion ?? DBNull.Value);
        p.AddWithValue("run", NpgsqlDbType.Uuid, (object?)decision.LabRunId ?? DBNull.Value);
        p.AddWithValue("edited", NpgsqlDbType.Boolean, decision.Edited);
        p.AddWithValue("auto", NpgsqlDbType.Boolean, decision.Auto);
        p.AddWithValue("settings", NpgsqlDbType.Bigint, (object?)decision.SettingsId ?? DBNull.Value);
        p.AddWithValue("hash", NpgsqlDbType.Char, (object?)decision.SnapshotHash ?? DBNull.Value);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, decision.Actor);
        var created = await insert.ExecuteNonQueryAsync(cancellationToken) == 1;
        var stored = await DecisionAsync(decision.DraftId, cancellationToken) ?? throw new InvalidOperationException("The decision was not found after it was stored.");
        return (stored, created);
    }

    private static async Task<List<StoredDraft>> ReadAsync(NpgsqlCommand select, CancellationToken cancellationToken)
    {
        var list = new List<StoredDraft>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new StoredDraft(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                JsonSerializer.Deserialize<DraftAnswer>(reader.GetString(7), Json), JsonSerializer.Deserialize<List<DraftAttempt>>(reader.GetString(8), Json) ?? [], JsonSerializer.Deserialize<List<StrategyIssue>>(reader.GetString(9), Json) ?? [],
                JsonSerializer.Deserialize<List<DraftFinding>>(reader.GetString(10), Json) ?? [], reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetString(12), reader.GetString(13), reader.GetString(14), reader.GetFieldValue<DateTimeOffset>(15)));
        }

        return list;
    }
}
