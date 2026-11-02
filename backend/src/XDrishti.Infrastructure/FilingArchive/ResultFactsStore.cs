using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// <c>filings.result_parses</c> and <c>filings.result_facts</c> (P15-03, ADR 0184), the only writer of both; insert-only for the runtime role. One filing is one transaction: the parse row first (<c>ON CONFLICT DO NOTHING</c>; a row that was already there ends the call), then its
/// facts. A crash leaves no half set.
/// </summary>
internal sealed class ResultFactsStore(NpgsqlDataSource dataSource) : IResultFactsStore
{
    public async ValueTask<IReadOnlyList<PendingResultFiling>> PendingAsync(string parserVersion, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT f.id, f.period_end, f.content_sha256, f.file_name FROM filings.filings f
            WHERE f.kind = 'results' AND NOT EXISTS (SELECT 1 FROM filings.result_parses p WHERE p.filing_id = f.id AND p.parser_version = @v)
            ORDER BY f.ingested_at, f.id LIMIT @limit
            """);
        command.Parameters.AddWithValue("v", parserVersion);
        command.Parameters.AddWithValue("limit", limit);
        var rows = new List<PendingResultFiling>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new PendingResultFiling(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1), reader.GetString(2), reader.GetString(3)));
        }

        return rows;
    }

    public async ValueTask<bool> SaveAsync(Guid filingId, string parserVersion, ParsedResults parsed, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var parse = new NpgsqlCommand("INSERT INTO filings.result_parses (filing_id, parser_version, outcome, reason, facts, flags, parsed_at) VALUES (@id, @v, @outcome, @reason, @facts, @flags, @at) ON CONFLICT (filing_id, parser_version) DO NOTHING", connection, transaction))
        {
            parse.Parameters.AddWithValue("id", filingId);
            parse.Parameters.AddWithValue("v", parserVersion);
            parse.Parameters.AddWithValue("outcome", parsed.Outcome);
            parse.Parameters.Add(new NpgsqlParameter("reason", NpgsqlDbType.Varchar) { Value = (object?)parsed.Reason ?? DBNull.Value });
            parse.Parameters.AddWithValue("facts", parsed.Facts.Count);
            parse.Parameters.Add(new NpgsqlParameter("flags", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(parsed.Flags) });
            parse.Parameters.AddWithValue("at", at.UtcDateTime);
            if (await parse.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using var batch = new NpgsqlBatch(connection, transaction);
        foreach (var fact in parsed.Facts)
        {
            var insert = new NpgsqlBatchCommand(
                "INSERT INTO filings.result_facts (filing_id, parser_version, field_code, span, basis, value, unit, state, unavailable_reason) VALUES (@id, @v, @field, @span, @basis, @value, @unit, @state, @reason) ON CONFLICT (filing_id, parser_version, field_code, span, basis) DO NOTHING");
            insert.Parameters.AddWithValue("id", filingId);
            insert.Parameters.AddWithValue("v", parserVersion);
            insert.Parameters.AddWithValue("field", fact.Field);
            insert.Parameters.AddWithValue("span", fact.Span);
            insert.Parameters.AddWithValue("basis", fact.Basis);
            insert.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Numeric) { Value = fact.Value is { } v ? v : DBNull.Value });
            insert.Parameters.AddWithValue("unit", fact.Unit);
            insert.Parameters.AddWithValue("state", fact.IsValue ? "value" : "unavailable");
            insert.Parameters.Add(new NpgsqlParameter("reason", NpgsqlDbType.Varchar) { Value = (object?)fact.Reason ?? DBNull.Value });
            batch.BatchCommands.Add(insert);
        }

        if (batch.BatchCommands.Count > 0)
        {
            await batch.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
