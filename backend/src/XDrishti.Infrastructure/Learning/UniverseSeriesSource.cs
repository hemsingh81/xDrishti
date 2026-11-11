using Npgsql;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;

namespace XDrishti.Infrastructure.Learning;

/// <summary>
/// The daily traded values (close x volume, rupees) of the tracked, not retired shares and ETFs between two India dates (BK-11, ADR 0261): one query, read only. The set is the tracked set now (stated limit: survivorship, as every simulation on a
/// current basket); a share is in a date's set from its first stored daily bar. Reads <c>market.bars_agg</c> day bars only.
/// </summary>
internal sealed class UniverseSeriesSource(NpgsqlDataSource dataSource) : IUniverseSeriesSource
{
    // shortcut: the set is the tracked, live set now, so a share that was delisted or tracked later is missing from earlier dates (survivorship, stated in ADR 0261)
    public async ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<SessionValue>>> ReadAsync(DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT a.instrument_id, a.ts, (a.close * a.volume)::numeric
            FROM market.bars_agg a
            JOIN market.tracked_instruments t ON t.instrument_id = a.instrument_id AND t.active
            JOIN market.instruments i ON i.id = a.instrument_id AND i.retired_at IS NULL AND i.type IN ('share', 'etf')
            WHERE a.timeframe = '1D' AND a.ts >= market.backfill_cut(a.instrument_id) AND a.ts >= @from AND a.ts < @to AND a.volume > 0
            ORDER BY a.instrument_id, a.ts
            """);
        command.Parameters.AddWithValue("from", new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).UtcDateTime);
        command.Parameters.AddWithValue("to", new DateTimeOffset(toExclusive.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).UtcDateTime);
        var series = new Dictionary<Guid, List<SessionValue>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            if (!series.TryGetValue(id, out var list))
            {
                series[id] = list = [];
            }

            list.Add(new SessionValue(IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(1)), reader.GetDecimal(2)));
        }

        return series.ToDictionary(p => p.Key, p => (IReadOnlyList<SessionValue>)p.Value);
    }
}
