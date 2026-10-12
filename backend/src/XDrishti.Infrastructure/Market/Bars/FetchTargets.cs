using Npgsql;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>The instruments the Dhan phase fetches: tracked now, not retired, a share, ETF or index, with a Dhan security id.</summary>
internal sealed class FetchTargets(NpgsqlDataSource dataSource) : IFetchTargets
{
    public async ValueTask<IReadOnlyList<FetchTarget>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT i.id, i.canonical_symbol, i.type, b.broker_id, b.segment, c.last_bar
            FROM market.tracked_instruments t
            JOIN market.instruments i ON i.id = t.instrument_id
            JOIN market.instrument_broker_ids b ON b.instrument_id = i.id AND b.broker = 'dhan'
            LEFT JOIN market.bar_coverage c ON c.instrument_id = i.id
            WHERE t.active AND i.retired_at IS NULL AND i.type IN ('share', 'etf', 'index')
            ORDER BY i.canonical_symbol
            """);
        var targets = new List<FetchTarget>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            targets.Add(new FetchTarget(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return targets;
    }
}
