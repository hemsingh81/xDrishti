using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant.Grounding;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// The audit of chat turns and its reads (spec P9-03 AC 7 and 10), raw SQL on <c>ai.*</c>. Insert and select only: the runtime role cannot update or delete a row of these tables. A turn start inserts the prompt (once per hash), the conversation (once) and the user's message in one transaction;
/// each tool call is one row; the assistant's message is inserted once at the end. Text with a NUL character (which PostgreSQL cannot store) is stored with U+FFFD in its place.
/// </summary>
internal sealed class ConversationStore(NpgsqlDataSource dataSource) : IConversationStore, IConversationReader
{
    private const int TitleLength = 120;
    private static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

    public async ValueTask<bool> ExistsAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM ai.conversations WHERE id = @id)");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, conversationId);
        return await select.ExecuteScalarAsync(cancellationToken) is true;
    }

    public async ValueTask<IReadOnlyList<HistoryMessage>> RecentAsync(Guid conversationId, int count, CancellationToken cancellationToken)
    {
        var messages = new List<(Guid Turn, string Role, string Content)>();
        await using (var select = dataSource.CreateCommand("SELECT turn_id, role, content FROM ai.messages WHERE conversation_id = @id ORDER BY seq DESC LIMIT @count"))
        {
            select.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, conversationId);
            select.Parameters.AddWithValue("count", NpgsqlDbType.Integer, Math.Clamp(count, 1, 50));
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        messages.Reverse();
        var turns = messages.Where(m => m.Role == "assistant").Select(m => m.Turn).Distinct().ToArray();
        var calls = new Dictionary<Guid, List<HistoryToolCall>>();
        if (turns.Length > 0)
        {
            await using var select = dataSource.CreateCommand("SELECT turn_id, call_index, tool_name, arguments::text, status, rows FROM ai.tool_calls WHERE conversation_id = @id AND turn_id = ANY(@turns) ORDER BY turn_id, call_index");
            select.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, conversationId);
            select.Parameters.AddWithValue("turns", NpgsqlDbType.Array | NpgsqlDbType.Uuid, turns);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var turn = reader.GetGuid(0);
                if (!calls.TryGetValue(turn, out var list))
                {
                    calls[turn] = list = [];
                }

                list.Add(new HistoryToolCall(reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5)));
            }
        }

        return [.. messages.Select(m => new HistoryMessage(m.Role, m.Content, m.Role == "assistant" && calls.TryGetValue(m.Turn, out var own) ? own : []))];
    }

    public async ValueTask<StartedTurn> StartTurnAsync(StartTurnRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var prompt = new NpgsqlCommand("INSERT INTO ai.prompts (hash, name, version, text) VALUES (@hash, @name, @version, @text) ON CONFLICT (hash) DO NOTHING", connection, transaction))
        {
            prompt.Parameters.AddWithValue("hash", NpgsqlDbType.Char, record.PromptHash);
            prompt.Parameters.AddWithValue("name", NpgsqlDbType.Varchar, record.PromptName);
            prompt.Parameters.AddWithValue("version", NpgsqlDbType.Varchar, record.PromptVersion);
            prompt.Parameters.AddWithValue("text", NpgsqlDbType.Text, Clean(record.PromptText));
            await prompt.ExecuteNonQueryAsync(cancellationToken);
        }

        var conversation = record.ConversationId ?? Guid.CreateVersion7();
        if (record.ConversationId is null)
        {
            await using var insert = new NpgsqlCommand("INSERT INTO ai.conversations (id, created_by, title, origin, settings_id, model_id, llm_build) VALUES (@id, @by, @title, @origin, @settings, @model, @build)", connection, transaction);
            insert.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, conversation);
            insert.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, record.Actor);
            insert.Parameters.AddWithValue("title", NpgsqlDbType.Varchar, Title(record.Question));
            insert.Parameters.AddWithValue("origin", NpgsqlDbType.Varchar, record.Origin);
            insert.Parameters.AddWithValue("settings", NpgsqlDbType.Bigint, (object?)record.SettingsId ?? DBNull.Value);
            insert.Parameters.AddWithValue("model", NpgsqlDbType.Varchar, record.ModelId);
            insert.Parameters.AddWithValue("build", NpgsqlDbType.Varchar, (object?)record.LlmBuild ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var seq = await NextSeqAsync(connection, transaction, conversation, cancellationToken);
        long messageId;
        await using (var user = new NpgsqlCommand("INSERT INTO ai.messages (conversation_id, turn_id, seq, role, content, prompt_hash) VALUES (@c, @turn, @seq, 'user', @content, @hash) RETURNING id", connection, transaction))
        {
            user.Parameters.AddWithValue("c", NpgsqlDbType.Uuid, conversation);
            user.Parameters.AddWithValue("turn", NpgsqlDbType.Uuid, record.TurnId);
            user.Parameters.AddWithValue("seq", NpgsqlDbType.Integer, seq);
            user.Parameters.AddWithValue("content", NpgsqlDbType.Text, Clean(record.Question));
            user.Parameters.AddWithValue("hash", NpgsqlDbType.Char, record.PromptHash);
            messageId = (long)(await user.ExecuteScalarAsync(cancellationToken))!;
        }

        await transaction.CommitAsync(cancellationToken);
        return new StartedTurn(conversation, messageId, seq);
    }

    public async ValueTask<long> RecordToolCallAsync(ToolCallRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var insert = dataSource.CreateCommand(
            "INSERT INTO ai.tool_calls (conversation_id, turn_id, call_index, tool_name, arguments, status, error_code, result, result_bytes, rows, truncated, evidence, redactions, injection_flags, started_at, duration_ms) VALUES (@c, @turn, @idx, @tool, @args, @status, @code, @result, @bytes, @rows, @trunc, @evidence, @red, @inj, @at, @ms) RETURNING id");
        var p = insert.Parameters;
        p.AddWithValue("c", NpgsqlDbType.Uuid, record.ConversationId);
        p.AddWithValue("turn", NpgsqlDbType.Uuid, record.TurnId);
        p.AddWithValue("idx", NpgsqlDbType.Integer, record.CallIndex);
        p.AddWithValue("tool", NpgsqlDbType.Varchar, record.Tool.Length > 80 ? record.Tool[..80] : record.Tool);
        p.AddWithValue("args", NpgsqlDbType.Jsonb, JsonText(record.ArgumentsJson) ?? "{}");
        p.AddWithValue("status", NpgsqlDbType.Varchar, record.Status);
        p.AddWithValue("code", NpgsqlDbType.Varchar, (object?)(record.ErrorCode is { Length: > 60 } c ? c[..60] : record.ErrorCode) ?? DBNull.Value);
        p.AddWithValue("result", NpgsqlDbType.Jsonb, (object?)JsonText(record.ResultJson) ?? DBNull.Value);
        p.AddWithValue("bytes", NpgsqlDbType.Integer, record.ResultBytes);
        p.AddWithValue("rows", NpgsqlDbType.Integer, record.Rows);
        p.AddWithValue("trunc", NpgsqlDbType.Boolean, record.Truncated);
        p.AddWithValue("evidence", NpgsqlDbType.Jsonb, (object?)JsonText(record.EvidenceJson) ?? DBNull.Value);
        p.AddWithValue("red", NpgsqlDbType.Integer, record.Redactions);
        p.AddWithValue("inj", NpgsqlDbType.Integer, record.InjectionFlags);
        p.AddWithValue("at", NpgsqlDbType.TimestampTz, record.StartedAt);
        p.AddWithValue("ms", NpgsqlDbType.Integer, record.DurationMs);
        return (long)(await insert.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask<long> CompleteTurnAsync(AssistantMessageRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var seq = await NextSeqAsync(connection, transaction, record.ConversationId, cancellationToken);
        await using var insert = new NpgsqlCommand(
            "INSERT INTO ai.messages (conversation_id, turn_id, seq, role, content, prompt_hash, model_id, prompt_tokens, completion_tokens, first_token_ms, total_ms, finish_reason, grounding_status, ungrounded) VALUES (@c, @turn, @seq, 'assistant', @content, @hash, @model, @pt, @ct, @ft, @ms, @finish, @grounding, @ungrounded) RETURNING id", connection, transaction);
        var p = insert.Parameters;
        p.AddWithValue("c", NpgsqlDbType.Uuid, record.ConversationId);
        p.AddWithValue("turn", NpgsqlDbType.Uuid, record.TurnId);
        p.AddWithValue("seq", NpgsqlDbType.Integer, seq);
        p.AddWithValue("content", NpgsqlDbType.Text, Clean(record.Content));
        p.AddWithValue("hash", NpgsqlDbType.Char, record.PromptHash);
        p.AddWithValue("model", NpgsqlDbType.Varchar, record.ModelId);
        p.AddWithValue("pt", NpgsqlDbType.Integer, (object?)record.PromptTokens ?? DBNull.Value);
        p.AddWithValue("ct", NpgsqlDbType.Integer, (object?)record.CompletionTokens ?? DBNull.Value);
        p.AddWithValue("ft", NpgsqlDbType.Integer, (object?)record.FirstTokenMs ?? DBNull.Value);
        p.AddWithValue("ms", NpgsqlDbType.Integer, record.TotalMs);
        p.AddWithValue("finish", NpgsqlDbType.Varchar, record.FinishReason);
        p.AddWithValue("grounding", NpgsqlDbType.Varchar, record.GroundingStatus);
        p.AddWithValue("ungrounded", NpgsqlDbType.Jsonb, JsonText(record.UngroundedJson) ?? "[]");
        var id = (long)(await insert.ExecuteScalarAsync(cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async ValueTask<IReadOnlyList<ConversationSummary>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT c.id, c.title, c.origin, c.created_at, count(m.id)::int, COALESCE(max(m.created_at), c.created_at)
            FROM (SELECT id, title, origin, created_at FROM ai.conversations ORDER BY created_at DESC LIMIT @limit) c
            LEFT JOIN ai.messages m ON m.conversation_id = c.id
            GROUP BY c.id, c.title, c.origin, c.created_at
            ORDER BY c.created_at DESC
            """);
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 200));
        var rows = new List<ConversationSummary>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ConversationSummary(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetInt32(4), reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return rows;
    }

    public async ValueTask<ConversationDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        string title;
        string origin;
        string model;
        long? settings;
        DateTimeOffset created;
        await using (var head = new NpgsqlCommand("SELECT title, origin, created_at, model_id, settings_id FROM ai.conversations WHERE id = @id", connection))
        {
            head.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
            await using var reader = await head.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            title = reader.GetString(0);
            origin = reader.GetString(1);
            created = reader.GetFieldValue<DateTimeOffset>(2);
            model = reader.GetString(3);
            settings = reader.IsDBNull(4) ? null : reader.GetInt64(4);
        }

        var calls = new Dictionary<Guid, List<ToolCallSummary>>();
        await using (var select = new NpgsqlCommand("SELECT id, turn_id, call_index, tool_name, arguments::text, status, error_code, rows, truncated, duration_ms, evidence::text FROM ai.tool_calls WHERE conversation_id = @id ORDER BY turn_id, call_index", connection))
        {
            select.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var turn = reader.GetGuid(1);
                if (!calls.TryGetValue(turn, out var list))
                {
                    calls[turn] = list = [];
                }

                list.Add(new ToolCallSummary(reader.GetInt64(0), reader.GetInt32(2), reader.GetString(3), Parse(reader.GetString(4)), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt32(7), reader.GetBoolean(8), reader.GetInt32(9), reader.IsDBNull(10) ? null : Parse(reader.GetString(10))));
            }
        }

        var raw = new List<RawMessage>();
        await using (var select = new NpgsqlCommand("SELECT id, turn_id, seq, role, content, created_at, finish_reason, grounding_status, ungrounded::text, prompt_tokens, completion_tokens, first_token_ms, total_ms, prompt_hash FROM ai.messages WHERE conversation_id = @id ORDER BY seq", connection))
        {
            select.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                raw.Add(new RawMessage(
                    reader.GetInt64(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.IsDBNull(10) ? null : reader.GetInt32(10), reader.IsDBNull(11) ? null : reader.GetInt32(11), reader.IsDBNull(12) ? null : reader.GetInt32(12), reader.IsDBNull(13) ? null : reader.GetString(13)));
            }
        }

        var answered = raw.Where(m => m.Role == "assistant").Select(m => m.Turn).ToHashSet();
        var messages = raw.Select(m => new MessageView(
            m.Id, m.Turn, m.Seq, m.Role, m.Content, m.CreatedAt, m.FinishReason, m.Grounding, Spans(m.Ungrounded), m.PromptTokens, m.CompletionTokens, m.FirstTokenMs, m.TotalMs, m.PromptHash,
            m.Role == "user" && !answered.Contains(m.Turn),
            m.Role == "assistant" && calls.TryGetValue(m.Turn, out var own) ? own : [])).ToList();
        return new ConversationDetail(id, title, origin, created, model, settings, messages);
    }

    public async ValueTask<StoredToolCall?> GetToolCallAsync(long id, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, conversation_id, turn_id, call_index, tool_name, arguments::text, status, error_code, result::text, result_bytes, rows, truncated, evidence::text, redactions, injection_flags, started_at, duration_ms FROM ai.tool_calls WHERE id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredToolCall(
            reader.GetInt64(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetInt32(3), reader.GetString(4), Parse(reader.GetString(5)), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : Parse(reader.GetString(8)), reader.GetInt32(9), reader.GetInt32(10), reader.GetBoolean(11), reader.IsDBNull(12) ? null : Parse(reader.GetString(12)), reader.GetInt32(13), reader.GetInt32(14),
            reader.GetFieldValue<DateTimeOffset>(15), reader.GetInt32(16));
    }

    private static async Task<int> NextSeqAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid conversation, CancellationToken cancellationToken)
    {
        await using var next = new NpgsqlCommand("SELECT COALESCE(max(seq), 0) + 1 FROM ai.messages WHERE conversation_id = @c", connection, transaction);
        next.Parameters.AddWithValue("c", NpgsqlDbType.Uuid, conversation);
        return (int)(await next.ExecuteScalarAsync(cancellationToken))!;
    }

    private static string Title(string question)
    {
        var line = string.Join(' ', question.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        line = Clean(line);
        if (line.Length == 0)
        {
            return "(empty)";
        }

        return line.Length <= TitleLength ? line : string.Concat(line.AsSpan(0, TitleLength - 3), "...");
    }

    private static string Clean(string text) => text.Replace('\0', '�');

    /// <summary>The text as valid jsonb input: a text that is not JSON (or holds the escape of NUL) is stored as a JSON string.</summary>
    private static string? JsonText(string? text)
    {
        if (text is null)
        {
            return null;
        }

        try
        {
            using var _ = JsonDocument.Parse(text);
            return text.Replace("\\u0000", "\\uFFFD", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(Clean(text));
        }
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static List<UngroundedSpan> Spans(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<UngroundedSpan>>(json, Camel) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record RawMessage(
        long Id, Guid Turn, int Seq, string Role, string Content, DateTimeOffset CreatedAt, string? FinishReason, string? Grounding, string? Ungrounded, int? PromptTokens, int? CompletionTokens, int? FirstTokenMs, int? TotalMs, string? PromptHash);
}
