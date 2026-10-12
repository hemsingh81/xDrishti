using Npgsql;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>
/// The 1-minute bars per India day (<see cref="IBarDayCounts"/>) without reading the minutes: the daily aggregate (<c>bars_agg</c>, timeframe 1D) stores how many 1-minute bars each day
/// has, so a month of days is a few dozen rows. Only the days inside the instrument's changed range (<c>bar_coverage.dirty_from..dirty_to</c>, bars written since the aggregates were
/// built) are counted in <c>bars_1m</c>, using its primary key (instrument, time); that range is small (the latest fetch) unless a big load is waiting to be aggregated.
/// </summary>
internal sealed class BarDayCounts(NpgsqlDataSource dataSource) : IBarDayCounts
{
    public async ValueTask<IReadOnlyDictionary<DateOnly, int>> CountAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<DateOnly, int>();
        if (to < from)
        {
            return counts;
        }

        var fromTs = Midnight(from);
        var toTs = Midnight(to.AddDays(1));

        DateOnly? dirtyFrom = null;
        DateOnly? dirtyTo = null;
        await using (var dirty = dataSource.CreateCommand("SELECT dirty_from, dirty_to FROM market.bar_coverage WHERE instrument_id = @id AND dirty_from IS NOT NULL"))
        {
            dirty.Parameters.AddWithValue("id", instrumentId);
            await using var reader = await dirty.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                dirtyFrom = IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(0));
                dirtyTo = IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(1));
            }
        }

        await using (var aggregated = dataSource.CreateCommand("SELECT ts, bars FROM market.bars_agg WHERE instrument_id = @id AND timeframe = '1D' AND ts >= @from AND ts < @to"))
        {
            aggregated.Parameters.AddWithValue("id", instrumentId);
            aggregated.Parameters.AddWithValue("from", fromTs);
            aggregated.Parameters.AddWithValue("to", toTs);
            await using var reader = await aggregated.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var day = IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(0));
                if (!(dirtyFrom <= day && day <= dirtyTo))
                {
                    counts[day] = reader.GetInt32(1);
                }
            }
        }

        if (dirtyFrom is { } dirtyStart && dirtyTo is { } dirtyEnd && dirtyEnd >= from && dirtyStart <= to)
        {
            var start = dirtyStart > from ? dirtyStart : from;
            var end = dirtyEnd < to ? dirtyEnd : to;
            await using var minutes = dataSource.CreateCommand(
                """
                SELECT ((ts AT TIME ZONE 'UTC') + interval '330 minutes')::date AS day, count(*)
                FROM market.bars_1m
                WHERE instrument_id = @id AND ts >= @from AND ts < @to
                GROUP BY 1
                """);
            minutes.Parameters.AddWithValue("id", instrumentId);
            minutes.Parameters.AddWithValue("from", Midnight(start));
            minutes.Parameters.AddWithValue("to", Midnight(end.AddDays(1)));
            await using var reader = await minutes.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                counts[reader.GetFieldValue<DateOnly>(0)] = (int)reader.GetInt64(1);
            }
        }

        return counts;
    }

    private static DateTimeOffset Midnight(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime();
}
