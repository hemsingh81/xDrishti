using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// <c>ai.briefings</c> and <c>ai.briefing_attempts</c> behind <see cref="IBriefingStore"/> (spec P9-06 AC 10): raw SQL, insert and select only (the runtime role has no more). The first row of a (kind, subject) wins: a second insert is skipped by the unique index. A new
/// version is a new row (n+1), never a change. The briefing and its attempts are written in one transaction.
/// </summary>
internal sealed class BriefingStore(NpgsqlDataSource dataSource) : IBriefingStore
{
    private const string Columns = "id, kind, subject, version, source, reason, text, deterministic_text, facts_json::text, facts_hash, model_id, prompt_version, word_count, created_at";

    public async ValueTask<(StoredBriefing Stored, bool Created)> InsertAsync(NewBriefing briefing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(briefing);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long? id;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO ai.briefings (kind, subject, version, source, reason, text, deterministic_text, facts_json, facts_hash, model_id, model_sha256, prompt_version, params_json, word_count, built_by, created_at)
            VALUES (@kind, @subject,
                    CASE WHEN @new THEN COALESCE((SELECT MAX(version) FROM ai.briefings WHERE kind = @kind AND subject = @subject), 0) + 1 ELSE 1 END,
                    @source, @reason, @text, @det, @facts::jsonb, @hash, @model, @sha, @prompt, @params::jsonb, @words, @by, now())
            ON CONFLICT (kind, subject, version) DO NOTHING
            RETURNING id
            """, connection, transaction))
        {
            var p = insert.Parameters;
            p.AddWithValue("kind", NpgsqlDbType.Varchar, briefing.Kind);
            p.AddWithValue("subject", NpgsqlDbType.Varchar, briefing.Subject);
            p.AddWithValue("new", NpgsqlDbType.Boolean, briefing.NewVersion);
            p.AddWithValue("source", NpgsqlDbType.Varchar, briefing.Source);
            p.AddWithValue("reason", NpgsqlDbType.Varchar, (object?)briefing.Reason ?? DBNull.Value);
            p.AddWithValue("text", NpgsqlDbType.Text, briefing.Text);
            p.AddWithValue("det", NpgsqlDbType.Text, briefing.DeterministicText);
            p.AddWithValue("facts", NpgsqlDbType.Text, briefing.FactsJson);
            p.AddWithValue("hash", NpgsqlDbType.Char, briefing.FactsHash);
            p.AddWithValue("model", NpgsqlDbType.Varchar, (object?)briefing.ModelId ?? DBNull.Value);
            p.AddWithValue("sha", NpgsqlDbType.Char, (object?)briefing.ModelSha256 ?? DBNull.Value);
            p.AddWithValue("prompt", NpgsqlDbType.Varchar, briefing.PromptVersion);
            p.AddWithValue("params", NpgsqlDbType.Text, briefing.ParamsJson);
            p.AddWithValue("words", NpgsqlDbType.Integer, briefing.WordCount);
            p.AddWithValue("by", NpgsqlDbType.Varchar, briefing.BuiltBy);
            id = await insert.ExecuteScalarAsync(cancellationToken) as long?;
        }

        if (id is { } briefingId)
        {
            foreach (var attempt in briefing.Attempts)
            {
                await using var row = new NpgsqlCommand(
                    "INSERT INTO ai.briefing_attempts (briefing_id, attempt, text, passed, findings_json, latency_ms, prompt_tokens, completion_tokens) VALUES (@id, @n, @text, @passed, @findings::jsonb, @ms, @pt, @ct)", connection, transaction);
                var p = row.Parameters;
                p.AddWithValue("id", NpgsqlDbType.Bigint, briefingId);
                p.AddWithValue("n", NpgsqlDbType.Integer, attempt.Attempt);
                p.AddWithValue("text", NpgsqlDbType.Text, attempt.Text);
                p.AddWithValue("passed", NpgsqlDbType.Boolean, attempt.Passed);
                p.AddWithValue("findings", NpgsqlDbType.Text, JsonSerializer.Serialize(attempt.Findings));
                p.AddWithValue("ms", NpgsqlDbType.Integer, attempt.LatencyMs);
                p.AddWithValue("pt", NpgsqlDbType.Integer, (object?)attempt.PromptTokens ?? DBNull.Value);
                p.AddWithValue("ct", NpgsqlDbType.Integer, (object?)attempt.CompletionTokens ?? DBNull.Value);
                await row.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        var stored = await FindAsync(briefing.Kind, briefing.Subject, cancellationToken) ?? throw new InvalidOperationException("The briefing was not found after it was stored.");
        return (stored, id is not null);
    }

    public async ValueTask<StoredBriefing?> FindAsync(string kind, string subject, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM ai.briefings WHERE kind = @kind AND subject = @subject ORDER BY version DESC LIMIT 1");
        select.Parameters.AddWithValue("kind", NpgsqlDbType.Varchar, kind);
        select.Parameters.AddWithValue("subject", NpgsqlDbType.Varchar, subject);
        var rows = await ReadAsync(select, cancellationToken);
        return rows.Count == 0 ? null : (await WithAttemptsAsync(rows, cancellationToken))[0];
    }

    public async ValueTask<IReadOnlyList<StoredBriefing>> ListAsync(string? kind, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM ai.briefings WHERE (@kind::text IS NULL OR kind = @kind) ORDER BY created_at DESC, id DESC LIMIT @limit");
        select.Parameters.AddWithValue("kind", NpgsqlDbType.Varchar, (object?)kind ?? DBNull.Value);
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        return await WithAttemptsAsync(await ReadAsync(select, cancellationToken), cancellationToken);
    }

    private static async Task<List<StoredBriefing>> ReadAsync(NpgsqlCommand select, CancellationToken cancellationToken)
    {
        var list = new List<StoredBriefing>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new StoredBriefing(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11), reader.GetInt32(12), reader.GetFieldValue<DateTimeOffset>(13), []));
        }

        return list;
    }

    private async Task<IReadOnlyList<StoredBriefing>> WithAttemptsAsync(List<StoredBriefing> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        await using var select = dataSource.CreateCommand("SELECT briefing_id, attempt, text, passed, findings_json::text, latency_ms, prompt_tokens, completion_tokens FROM ai.briefing_attempts WHERE briefing_id = ANY(@ids) ORDER BY briefing_id, attempt");
        select.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, rows.Select(r => r.Id).ToArray());
        var byBriefing = new Dictionary<long, List<BriefingAttemptRecord>>();
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var findings = JsonSerializer.Deserialize<List<StoredFinding>>(reader.GetString(4)) ?? [];
                var attempt = new BriefingAttemptRecord(reader.GetInt32(1), reader.GetString(2), reader.GetBoolean(3), findings, reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetInt32(7));
                if (!byBriefing.TryGetValue(reader.GetInt64(0), out var list))
                {
                    byBriefing[reader.GetInt64(0)] = list = [];
                }

                list.Add(attempt);
            }
        }

        return [.. rows.Select(r => r with { Attempts = byBriefing.TryGetValue(r.Id, out var a) ? a : [] })];
    }
}
