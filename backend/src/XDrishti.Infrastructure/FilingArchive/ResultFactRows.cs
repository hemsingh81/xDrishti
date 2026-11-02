using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// Stored parse rows and facts (P15-03). SELECT only. The instrument read narrows the envelopes by the instrument, the kind and the cutoff <c>t - lag</c> on <c>broadcast_ts</c> (plus those with no time, which are counted); the facts are those of the given parser version of
/// the envelopes with a time at or before the cutoff. <see cref="ResultFactsAsOfRule"/> decides.
/// </summary>
internal sealed class ResultFactRows(NpgsqlDataSource dataSource) : IResultFactRows
{
    public async ValueTask<IReadOnlyList<ResultFilingFacts>> ForInstrumentAsync(Guid instrumentId, DateTimeOffset cutoff, string parserVersion, CancellationToken cancellationToken)
    {
        var filings = new List<StoredFiling>();
        await using (var command = dataSource.CreateCommand($"SELECT {FilingRows.Columns} FROM filings.filings WHERE instrument_id = @i AND kind = 'results' AND (broadcast_ts <= @cutoff OR broadcast_ts IS NULL)"))
        {
            command.Parameters.AddWithValue("i", instrumentId);
            command.Parameters.Add(new NpgsqlParameter("cutoff", NpgsqlDbType.TimestampTz) { Value = cutoff.UtcDateTime });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                filings.Add(FilingRows.Read(reader));
            }
        }

        var facts = new Dictionary<Guid, List<StoredResultFact>>();
        await using (var command = dataSource.CreateCommand(
            """
            SELECT x.filing_id, x.field_code, x.span, x.basis, x.value, x.unit, x.unavailable_reason
            FROM filings.result_facts x JOIN filings.filings f ON f.id = x.filing_id
            WHERE f.instrument_id = @i AND f.kind = 'results' AND f.broadcast_ts <= @cutoff AND x.parser_version = @v
            """))
        {
            command.Parameters.AddWithValue("i", instrumentId);
            command.Parameters.Add(new NpgsqlParameter("cutoff", NpgsqlDbType.TimestampTz) { Value = cutoff.UtcDateTime });
            command.Parameters.AddWithValue("v", parserVersion);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                if (!facts.TryGetValue(id, out var list))
                {
                    facts[id] = list = [];
                }

                list.Add(Fact(reader, 1));
            }
        }

        return [.. filings.Select(f => new ResultFilingFacts(f, facts.TryGetValue(f.Id, out var list) ? list : []))];
    }

    public async ValueTask<StoredParse?> ParseOfAsync(Guid filingId, string parserVersion, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT outcome, reason, facts, flags::text FROM filings.result_parses WHERE filing_id = @id AND parser_version = @v");
        command.Parameters.AddWithValue("id", filingId);
        command.Parameters.AddWithValue("v", parserVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var flags = JsonSerializer.Deserialize<Dictionary<string, int>>(reader.GetString(3)) ?? [];
        return new StoredParse(filingId, parserVersion, reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt32(2), flags);
    }

    public async ValueTask<IReadOnlyList<StoredResultFact>> FactsOfAsync(Guid filingId, string parserVersion, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT field_code, span, basis, value, unit, unavailable_reason FROM filings.result_facts WHERE filing_id = @id AND parser_version = @v");
        command.Parameters.AddWithValue("id", filingId);
        command.Parameters.AddWithValue("v", parserVersion);
        var rows = new List<StoredResultFact>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Fact(reader, 0));
        }

        return [.. rows.OrderBy(f => ResultCodes.Spans.ToList().IndexOf(f.Span)).ThenBy(f => ResultCodes.Bases.ToList().IndexOf(f.Basis)).ThenBy(f => ResultCodes.Fields.ToList().IndexOf(f.Field))];
    }

    // field, span, basis, value, unit, reason from <paramref name="first"/>.
    private static StoredResultFact Fact(NpgsqlDataReader reader, int first) =>
        new(reader.GetString(first), reader.GetString(first + 1), reader.GetString(first + 2), reader.IsDBNull(first + 3) ? null : reader.GetDecimal(first + 3), reader.GetString(first + 4), reader.IsDBNull(first + 5) ? null : reader.GetString(first + 5));
}
