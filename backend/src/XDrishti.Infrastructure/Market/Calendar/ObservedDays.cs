using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Calendar;

namespace XDrishti.Infrastructure.Market.Calendar;

/// <summary>
/// The reads behind the observed proposals of the trading calendar (ADR 0017 §1). Dates come from the daily aggregates (one row per instrument and day, read with an index
/// lookup per instrument — never a scan of the 1-minute bars); only the few dates that might be special sessions have their 1-minute bars looked at, per instrument
/// through the primary key, so compressed chunks are read by segment. India dates throughout (a fixed UTC+05:30 offset).
/// </summary>
internal sealed class ObservedDays(NpgsqlDataSource dataSource) : IObservedDays
{
    private const string DailyRows =
        """
        SELECT c.instrument_id, (a.ts AT TIME ZONE 'UTC' + interval '330 minutes')::date AS d, a.bars
        FROM market.bar_coverage c
        CROSS JOIN LATERAL (SELECT ts, bars FROM market.bars_agg WHERE instrument_id = c.instrument_id AND timeframe = '1D') a
        """;

    public async ValueTask<DayPresence> ReadPresenceAsync(CancellationToken cancellationToken)
    {
        await using (var pending = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM market.bar_coverage WHERE dirty_from IS NOT NULL)"))
        {
            if (await pending.ExecuteScalarAsync(cancellationToken) is true)
            {
                return new DayPresence(true, [], []);
            }
        }

        var days = new List<DayCount>();
        await using (var perDay = dataSource.CreateCommand(
            $"""
            WITH rows AS ({DailyRows}),
                 spans AS (SELECT instrument_id, min(d) AS first_d, max(d) AS last_d FROM rows GROUP BY instrument_id)
            SELECT r.d, count(*)::int, (count(*) FILTER (WHERE r.d > s.first_d AND r.d < s.last_d))::int, (percentile_disc(0.5) WITHIN GROUP (ORDER BY r.bars))::int
            FROM rows r JOIN spans s USING (instrument_id)
            GROUP BY r.d
            ORDER BY r.d
            """))
        await using (var reader = await perDay.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                days.Add(new DayCount(reader.GetFieldValue<DateOnly>(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
            }
        }

        var spans = new List<InstrumentSpan>();
        await using (var perInstrument = dataSource.CreateCommand(
            $"WITH rows AS ({DailyRows}) SELECT min(d), max(d) FROM rows GROUP BY instrument_id"))
        await using (var reader = await perInstrument.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                spans.Add(new InstrumentSpan(reader.GetFieldValue<DateOnly>(0), reader.GetFieldValue<DateOnly>(1)));
            }
        }

        return new DayPresence(false, days, spans);
    }

    public async ValueTask<IReadOnlyDictionary<DateOnly, ObservedWindow>> ReadWindowsAsync(IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dates);
        var windows = new Dictionary<DateOnly, ObservedWindow>();
        if (dates.Count == 0)
        {
            return windows;
        }

        await using var command = dataSource.CreateCommand(
            """
            SELECT d.day, count(*)::int, (mode() WITHIN GROUP (ORDER BY x.first_m))::int, (mode() WITHIN GROUP (ORDER BY x.last_m))::int
            FROM unnest(@days) AS d(day)
            CROSS JOIN market.bar_coverage c
            CROSS JOIN LATERAL (
                SELECT (extract(epoch FROM min(b.ts))::bigint + 19800) % 86400 / 60 AS first_m,
                       (extract(epoch FROM max(b.ts))::bigint + 19800) % 86400 / 60 AS last_m
                FROM market.bars_1m b
                WHERE b.instrument_id = c.instrument_id
                  AND b.ts >= (d.day::timestamp - interval '330 minutes') AT TIME ZONE 'UTC'
                  AND b.ts < (d.day::timestamp - interval '330 minutes' + interval '1 day') AT TIME ZONE 'UTC'
                HAVING count(*) > 0
            ) x
            GROUP BY d.day
            """);
        command.Parameters.Add(new NpgsqlParameter("days", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = dates.ToArray() });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            windows[reader.GetFieldValue<DateOnly>(0)] = new ObservedWindow(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3) + 1);
        }

        return windows;
    }
}
