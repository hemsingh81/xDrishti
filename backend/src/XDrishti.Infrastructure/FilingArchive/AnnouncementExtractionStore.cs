using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// <c>filings.extractions</c> (spec P15-08), insert-only for the runtime role. One row is one extraction of one announcement by one model, prompt and schema; the primary key makes a repeat a no-op (<c>ON CONFLICT DO NOTHING</c>). The pending read is a plain SELECT. Only the checked
/// answer or a closed reason is written: no raw answer and no announcement text. No decision, plan, check or order reads this table.
/// </summary>
internal sealed class AnnouncementExtractionStore(NpgsqlDataSource dataSource) : IAnnouncementExtractionStore
{
    public async ValueTask<IReadOnlyList<Guid>> PendingAsync(DateTimeOffset from, DateTimeOffset toExclusive, ExtractionKey key, int skip, int take, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        await using var command = dataSource.CreateCommand(
            """
            SELECT f.id
            FROM filings.filings f
            JOIN filings.announcements a ON a.filing_id = f.id
            WHERE f.kind = 'announcement' AND f.broadcast_state = 'ok' AND f.broadcast_ts >= @from AND f.broadcast_ts < @to
              AND NOT EXISTS (SELECT 1 FROM filings.extractions e WHERE e.filing_id = f.id AND e.model_sha256 = @model AND e.prompt_hash = @prompt AND e.schema_version = @schema)
            ORDER BY f.broadcast_ts, f.id
            OFFSET @skip LIMIT @take
            """);
        var p = command.Parameters;
        p.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = from.UtcDateTime });
        p.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toExclusive.UtcDateTime });
        p.AddWithValue("model", NpgsqlDbType.Char, key.ModelSha256);
        p.AddWithValue("prompt", NpgsqlDbType.Char, key.PromptHash);
        p.AddWithValue("schema", NpgsqlDbType.Varchar, key.SchemaVersion);
        p.AddWithValue("skip", NpgsqlDbType.Integer, skip);
        p.AddWithValue("take", NpgsqlDbType.Integer, take);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    public async ValueTask<bool> InsertAsync(NewExtraction extraction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO filings.extractions (filing_id, model_sha256, prompt_name, prompt_version, prompt_hash, schema_version, state, reason, material, category, rationale, order_value_inr, guidance, rating_action, ungrounded, latency_ms, prompt_tokens, completion_tokens, created_at)
            VALUES (@filing, @model, @pname, @pversion, @prompt, @schema, @state, @reason, @material, @category, @rationale, @order, @guidance, @rating, @ungrounded, @ms, @pt, @ct, @at)
            ON CONFLICT (filing_id, model_sha256, prompt_hash, schema_version) DO NOTHING
            """);
        var p = command.Parameters;
        var answer = extraction.Answer;
        p.AddWithValue("filing", NpgsqlDbType.Uuid, extraction.FilingId);
        p.AddWithValue("model", NpgsqlDbType.Char, extraction.Key.ModelSha256);
        p.AddWithValue("pname", NpgsqlDbType.Varchar, extraction.Key.PromptName);
        p.AddWithValue("pversion", NpgsqlDbType.Varchar, extraction.Key.PromptVersion);
        p.AddWithValue("prompt", NpgsqlDbType.Char, extraction.Key.PromptHash);
        p.AddWithValue("schema", NpgsqlDbType.Varchar, extraction.Key.SchemaVersion);
        p.AddWithValue("state", NpgsqlDbType.Varchar, extraction.State);
        p.AddWithValue("reason", NpgsqlDbType.Varchar, (object?)extraction.Reason ?? DBNull.Value);
        p.AddWithValue("material", NpgsqlDbType.Boolean, answer is null ? DBNull.Value : (object)answer.Material);
        p.AddWithValue("category", NpgsqlDbType.Varchar, (object?)answer?.Category ?? DBNull.Value);
        p.AddWithValue("rationale", NpgsqlDbType.Varchar, (object?)answer?.Reason ?? DBNull.Value);
        p.AddWithValue("order", NpgsqlDbType.Numeric, (object?)answer?.OrderValueInr ?? DBNull.Value);
        p.AddWithValue("guidance", NpgsqlDbType.Varchar, (object?)answer?.Guidance ?? DBNull.Value);
        p.AddWithValue("rating", NpgsqlDbType.Varchar, (object?)answer?.RatingAction ?? DBNull.Value);
        p.AddWithValue("ungrounded", NpgsqlDbType.Boolean, extraction.Ungrounded);
        p.AddWithValue("ms", NpgsqlDbType.Integer, extraction.LatencyMs);
        p.AddWithValue("pt", NpgsqlDbType.Integer, (object?)extraction.PromptTokens ?? DBNull.Value);
        p.AddWithValue("ct", NpgsqlDbType.Integer, (object?)extraction.CompletionTokens ?? DBNull.Value);
        p.Add(new NpgsqlParameter("at", NpgsqlDbType.TimestampTz) { Value = extraction.CreatedAt.UtcDateTime });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<IReadOnlyList<ExtractionCoverageRow>> CoverageAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT to_char(f.broadcast_ts AT TIME ZONE 'Asia/Kolkata', 'YYYY-MM'), e.model_sha256, e.prompt_name, e.prompt_version, e.schema_version, e.state, count(*), count(*) FILTER (WHERE e.ungrounded)
            FROM filings.extractions e
            JOIN filings.filings f ON f.id = e.filing_id
            GROUP BY 1, 2, 3, 4, 5, 6
            ORDER BY 1 DESC, 2, 3, 4, 5, 6
            """);
        var rows = new List<ExtractionCoverageRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ExtractionCoverageRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), (int)reader.GetInt64(6), (int)reader.GetInt64(7)));
        }

        return rows;
    }
}
