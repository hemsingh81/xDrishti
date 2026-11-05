using Npgsql;
using XDrishti.Application.Abstractions.Shadow;

namespace XDrishti.Infrastructure.Paper;

/// <summary>The read of shadow outcomes behind <see cref="IShadowResultReader"/> (spec CF-08 part B, ADR 0212): select only, for the summary. Nothing that chooses what to approve may name the port (an architecture test).</summary>
internal sealed class ShadowResultReader(NpgsqlDataSource dataSource) : IShadowResultReader
{
    public async ValueTask<IReadOnlyList<ShadowResultRow>> ResultsAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT e.session_date, e.trade_key, e.ticket_group, e.counts_for_edge, COALESCE(g.status, 'pending'), g.r_sim, g.r_basis, g.sheet_id, g.r_gross
            FROM paper.shadow_entries e
            LEFT JOIN paper.shadow_grades g ON g.entry_id = e.id
            ORDER BY e.session_date, e.id
            """);
        var result = new List<ShadowResultRow>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ShadowResultRow(
                DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetDouble(8)));
        }

        return result;
    }

    public async ValueTask<ShadowChainTotals> ChainAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT (SELECT count(*) FROM paper.shadow_sessions), (SELECT count(*) FROM paper.shadow_sessions WHERE status = 'gap'),
                   (SELECT session_date FROM paper.shadow_sessions ORDER BY session_date DESC LIMIT 1), (SELECT digest FROM paper.shadow_sessions ORDER BY session_date DESC LIMIT 1)
            """);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new ShadowChainTotals((int)reader.GetInt64(0), (int)reader.GetInt64(1), reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)), reader.IsDBNull(3) ? null : reader.GetString(3));
    }
}
