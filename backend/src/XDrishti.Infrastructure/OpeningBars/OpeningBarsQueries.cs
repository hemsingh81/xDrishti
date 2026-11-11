using System.Globalization;
using Npgsql;
using XDrishti.Application.Abstractions.OpeningBars;
using XDrishti.Domain.Market;

namespace XDrishti.Infrastructure.OpeningBars;

/// <summary>
/// What the derived job reads of the opening minutes (BK-03b), raw SQL, no network: the live rows of a day, the history of the minutes 09:15 to 09:29 of earlier days (the stored 1-minute bars first, a live row only for a minute that has none there) and the day's events.
/// </summary>
internal sealed class OpeningBarsQueries(NpgsqlDataSource dataSource) : IOpeningBarsQueries
{
    public async ValueTask<IReadOnlyList<MinuteBar>> ReadDayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT instrument_id, minute_start, o, h, l, c, v, packets FROM market.bars_1m_live WHERE minute_start >= @from AND minute_start < @to");
        command.Parameters.AddWithValue("from", Start(day));
        command.Parameters.AddWithValue("to", Start(day.AddDays(1)));
        var rows = new List<MinuteBar>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MinuteBar(reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetInt64(6), reader.GetInt32(7)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<MinuteBar>> ReadHistoryAsync(IReadOnlyCollection<Guid> instruments, DateOnly from, DateOnly to, int minutes, IReadOnlyCollection<DateOnly> excludeLiveDays, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        if (instruments.Count == 0)
        {
            return [];
        }

        await using var command = dataSource.CreateCommand(
            """
            SELECT instrument_id, ts, open::numeric, high::numeric, low::numeric, close::numeric, volume::bigint, 1
            FROM market.bars_1m
            WHERE instrument_id = ANY(@ids) AND ts >= @from AND ts < @to
              AND (ts AT TIME ZONE 'Asia/Kolkata')::time >= time '09:15' AND (ts AT TIME ZONE 'Asia/Kolkata')::time < time '09:15' + make_interval(mins => @minutes)
            UNION ALL
            SELECT l.instrument_id, l.minute_start, l.o, l.h, l.l, l.c, l.v, l.packets
            FROM market.bars_1m_live l
            WHERE l.instrument_id = ANY(@ids) AND l.minute_start >= @from AND l.minute_start < @to
              AND (l.minute_start AT TIME ZONE 'Asia/Kolkata')::date <> ALL(@excluded)
              AND (l.minute_start AT TIME ZONE 'Asia/Kolkata')::time < time '09:15' + make_interval(mins => @minutes)
              AND NOT EXISTS (SELECT 1 FROM market.bars_1m b WHERE b.instrument_id = l.instrument_id AND b.ts = l.minute_start)
            """);
        command.Parameters.AddWithValue("ids", instruments.ToArray());
        command.Parameters.AddWithValue("minutes", minutes);
        command.Parameters.AddWithValue("excluded", excludeLiveDays.ToArray());
        command.Parameters.AddWithValue("from", Start(from));
        command.Parameters.AddWithValue("to", Start(to.AddDays(1)));
        var rows = new List<MinuteBar>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MinuteBar(reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5), Convert.ToInt64(reader.GetValue(6), CultureInfo.InvariantCulture), reader.GetInt32(7)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<OpeningBarsEvent>> ReadEventsAsync(DateOnly day, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT kind, from_at, to_at FROM market.opening_bar_events WHERE session_date = @day ORDER BY id");
        command.Parameters.AddWithValue("day", day);
        var rows = new List<OpeningBarsEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new OpeningBarsEvent(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyDictionary<DateOnly, IReadOnlyList<OpeningBarsEvent>>> ReadEventsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT session_date, kind, from_at, to_at FROM market.opening_bar_events WHERE session_date >= @from AND session_date <= @to ORDER BY id");
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        var days = new Dictionary<DateOnly, List<OpeningBarsEvent>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var day = reader.GetFieldValue<DateOnly>(0);
            if (!days.TryGetValue(day, out var list))
            {
                days[day] = list = [];
            }

            list.Add(new OpeningBarsEvent(reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return days.ToDictionary(d => d.Key, d => (IReadOnlyList<OpeningBarsEvent>)d.Value);
    }

    private static DateTimeOffset Start(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime();
}
