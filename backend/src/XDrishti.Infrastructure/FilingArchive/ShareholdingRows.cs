using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary><see cref="IShareholdingRows"/> (P15-04, ADR 0181): the stored shareholding groups of one instrument with a time at or before the cutoff <c>t - lag</c>, with their lines. The query only narrows; <c>ShareholdingAsOfRule</c> decides. SELECT only.</summary>
internal sealed class ShareholdingRows(NpgsqlDataSource dataSource) : IShareholdingRows
{
    public async ValueTask<IReadOnlyList<ShareholdingEnvelope>> GroupsAsync(Guid instrumentId, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var filings = new List<StoredFiling>();
        await using (var command = dataSource.CreateCommand($"SELECT {FilingRows.Qualified("f")} FROM filings.filings f WHERE f.instrument_id = @i AND f.kind = 'shareholding' AND f.broadcast_ts <= @cutoff AND EXISTS (SELECT 1 FROM filings.shareholding s WHERE s.filing_id = f.id)"))
        {
            command.Parameters.AddWithValue("i", instrumentId);
            command.Parameters.Add(new NpgsqlParameter("cutoff", NpgsqlDbType.TimestampTz) { Value = cutoff.UtcDateTime });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                filings.Add(FilingRows.Read(reader));
            }
        }

        if (filings.Count == 0)
        {
            return [];
        }

        var lines = new Dictionary<Guid, List<ShareholdingLine>>();
        await using (var command = dataSource.CreateCommand("SELECT filing_id, category, pct, pledged_pct FROM filings.shareholding WHERE filing_id = ANY(@ids)"))
        {
            command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = filings.Select(f => f.Id).ToArray() });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                if (!lines.TryGetValue(id, out var list))
                {
                    list = [];
                    lines[id] = list;
                }

                list.Add(new ShareholdingLine(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetDecimal(2), reader.IsDBNull(3) ? null : reader.GetDecimal(3)));
            }
        }

        return [.. filings.Select(f => new ShareholdingEnvelope(f, [.. lines.GetValueOrDefault(f.Id, []).OrderBy(l => Array.IndexOf(ShareholdingCsv.Categories, l.Category))]))];
    }
}
