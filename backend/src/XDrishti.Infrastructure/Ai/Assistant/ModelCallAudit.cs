using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>Writes <c>ai.model_calls</c> (spec P9-03): one insert-only row per model call outside chat. No content.</summary>
internal sealed class ModelCallAudit(NpgsqlDataSource dataSource) : IModelCallAudit
{
    public async ValueTask RecordAsync(ModelCallRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var insert = dataSource.CreateCommand(
            "INSERT INTO ai.model_calls (purpose, caller, prompt_hash, model_id, model_sha256, prompt_tokens, completion_tokens, total_ms, finish_reason, status, error_code) VALUES (@purpose, @caller, @hash, @model, @sha, @pt, @ct, @ms, @finish, @status, @code)");
        var p = insert.Parameters;
        p.AddWithValue("purpose", NpgsqlDbType.Varchar, record.Purpose);
        p.AddWithValue("caller", NpgsqlDbType.Varchar, record.Caller);
        p.AddWithValue("hash", NpgsqlDbType.Char, record.PromptHash);
        p.AddWithValue("model", NpgsqlDbType.Varchar, record.ModelId);
        p.AddWithValue("sha", NpgsqlDbType.Char, (object?)record.ModelSha256 ?? DBNull.Value);
        p.AddWithValue("pt", NpgsqlDbType.Integer, (object?)record.PromptTokens ?? DBNull.Value);
        p.AddWithValue("ct", NpgsqlDbType.Integer, (object?)record.CompletionTokens ?? DBNull.Value);
        p.AddWithValue("ms", NpgsqlDbType.Integer, record.TotalMs);
        p.AddWithValue("finish", NpgsqlDbType.Varchar, record.FinishReason);
        p.AddWithValue("status", NpgsqlDbType.Varchar, record.Status);
        p.AddWithValue("code", NpgsqlDbType.Varchar, (object?)record.ErrorCode ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
