using Npgsql;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Infrastructure.Market.Actions;

/// <summary>The day bars (the raw <c>1D</c> aggregate) for the detection of corporate actions: one index lookup per instrument, never a scan of the 1-minute bars.</summary>
internal sealed class DailySeries(NpgsqlDataSource dataSource) : IDailySeries
{
    public async ValueTask<IReadOnlyList<SeriesInstrument>> ListInstrumentsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT i.id, i.canonical_symbol
            FROM market.instruments i
            JOIN market.bar_coverage c ON c.instrument_id = i.id
            WHERE i.retired_at IS NULL AND i.type IN ('share', 'etf')
            ORDER BY i.canonical_symbol
            """);
        var list = new List<SeriesInstrument>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SeriesInstrument(reader.GetGuid(0), reader.GetString(1)));
        }

        return list;
    }

    public async ValueTask<IReadOnlyList<DailyBar>> ReadAsync(Guid instrumentId, DateOnly from, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT ts, open, high, low, close, volume FROM market.bars_agg WHERE instrument_id = @id AND timeframe = '1D' AND ts >= @from ORDER BY ts");
        command.Parameters.AddWithValue("id", instrumentId);
        command.Parameters.AddWithValue("from", new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime());
        var bars = new List<DailyBar>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            bars.Add(new DailyBar(IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(0)), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetInt64(5)));
        }

        return bars;
    }
}
