using Npgsql;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Domain.Market;

namespace XDrishti.Infrastructure.Depth;

/// <summary>
/// The most liquid tracked shares for the day's depth set (P7-16 criterion 5): the mean of close x volume over the last 20 daily bars before the given day, among tracked, not retired shares and ETFs with a Dhan NSE equity id, ties by symbol. Reads daily
/// bars and the master list only (read only).
/// </summary>
internal sealed class DepthUniverse(NpgsqlDataSource dataSource) : IDepthUniverse
{
    public const int Sessions = 20;

    public async ValueTask<IReadOnlyList<DepthTurnover>> MostLiquidAsync(DateOnly before, int take, CancellationToken cancellationToken)
    {
        if (take <= 0)
        {
            return [];
        }

        var until = new DateTimeOffset(before.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        await using var command = dataSource.CreateCommand(
            """
            WITH recent AS (
                SELECT a.instrument_id, a.close * a.volume AS turnover,
                       row_number() OVER (PARTITION BY a.instrument_id ORDER BY a.ts DESC) AS n
                FROM market.bars_agg a
                JOIN market.tracked_instruments t ON t.instrument_id = a.instrument_id AND t.active
                WHERE a.timeframe = '1D' AND a.ts < @until AND a.ts >= @since
            )
            SELECT i.canonical_symbol, i.id, b.broker_id, avg(r.turnover)::float8 AS mean_turnover
            FROM recent r
            JOIN market.instruments i ON i.id = r.instrument_id AND i.retired_at IS NULL AND i.type IN ('share', 'etf')
            JOIN market.instrument_broker_ids b ON b.instrument_id = i.id AND b.broker = 'dhan' AND b.segment = 'NSE_EQ'
            WHERE r.n <= @sessions
            GROUP BY i.canonical_symbol, i.id, b.broker_id
            ORDER BY mean_turnover DESC, i.canonical_symbol
            LIMIT @take
            """);
        command.Parameters.AddWithValue("until", until.UtcDateTime);
        command.Parameters.AddWithValue("since", until.AddDays(-60).UtcDateTime);
        command.Parameters.AddWithValue("sessions", Sessions);
        command.Parameters.AddWithValue("take", take);
        var rows = new List<DepthTurnover>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new DepthTurnover(new InstrumentSecurity(reader.GetString(0), reader.GetGuid(1), reader.GetString(2)), reader.GetDouble(3)));
        }

        return rows;
    }
}
