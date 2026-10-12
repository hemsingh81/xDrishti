using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>
/// Builds the derived timeframes from the stored 1-minute bars with set-based upserts (ADR 0016 §6): one statement for 5m, 10m, 15m, 30m, 1h and 1D from a single
/// read of the changed days, then 1W and 1M from the day bars. Idempotent: a bucket that comes out the same is not written. Everything for one instrument, its
/// coverage and its changed range is one transaction. The rules (also in docs/design/05 §4):
/// <list type="bullet">
/// <item>open is the first bar's open, high the highest, low the lowest, close the last bar's close, volume the sum; <c>bars</c> counts the 1-minute bars;</item>
/// <item>intraday buckets are anchored to the 09:15 India session open (5m, 10m, 15m, 30m: 09:15, 09:30 …; 1h: 09:15, 10:15 … 14:15, then 15:15–15:30, which holds only 15 bars);</item>
/// <item>the day bucket is the India date, the week starts on Monday, the month is the calendar month; day, week and month buckets start at India midnight;</item>
/// <item><c>complete</c>: an intraday bucket holds exactly the minutes of its window (clipped to the 15:30 close); a day has all 375 minutes of the regular session, or, for a day without any
/// (the Muhurat evening session), every minute between its first and last bar; a week or month is complete when all its days are.</item>
/// </list>
/// </summary>
internal sealed class BarAggregates(NpgsqlDataSource dataSource, TimeProvider clock) : IBarAggregates
{
    // 09:15 India (03:45 UTC) is the origin of the intraday buckets, midnight India (18:30 UTC the day before) of the days, Monday 2000-01-03 of the weeks.
    private const string IntradayOrigin = "TIMESTAMPTZ '2000-01-01 03:45:00+00'";

    private const string DayOrigin = "TIMESTAMPTZ '2000-01-01 18:30:00+00'";

    private const string WeekOrigin = "TIMESTAMPTZ '2000-01-02 18:30:00+00'";

    private static string MinuteOfDay(string timestamp) => $"((extract(epoch FROM {timestamp})::bigint + 19800) % 86400) / 60";

    private const string Upsert =
        """
        ON CONFLICT (instrument_id, timeframe, ts) DO UPDATE SET
            open = EXCLUDED.open, high = EXCLUDED.high, low = EXCLUDED.low, close = EXCLUDED.close,
            volume = EXCLUDED.volume, bars = EXCLUDED.bars, complete = EXCLUDED.complete
        WHERE (a.open, a.high, a.low, a.close, a.volume, a.bars, a.complete)
            IS DISTINCT FROM (EXCLUDED.open, EXCLUDED.high, EXCLUDED.low, EXCLUDED.close, EXCLUDED.volume, EXCLUDED.bars, EXCLUDED.complete)
        """;

    private static readonly string IntradayAndDay = BuildIntradayAndDay();

    private static readonly string Weeks =
        $"""
        WITH d AS MATERIALIZED (
            SELECT ts, open, high, low, close, volume, bars, complete FROM market.bars_agg
            WHERE instrument_id = @id AND timeframe = '1D' AND ts >= @from AND ts < @to),
        w AS (
            INSERT INTO market.bars_agg AS a (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete)
            SELECT @id, '1W', date_bin(interval '7 days', ts, {WeekOrigin}), first(open, ts), max(high), min(low), last(close, ts),
                   sum(volume)::bigint, sum(bars)::integer, bool_and(complete)
            FROM d GROUP BY date_bin(interval '7 days', ts, {WeekOrigin})
            {Upsert}
            RETURNING timeframe)
        SELECT timeframe, count(*) FROM w GROUP BY timeframe
        """;

    private static readonly string Months =
        $"""
        WITH d AS MATERIALIZED (
            SELECT ts, open, high, low, close, volume, bars, complete FROM market.bars_agg
            WHERE instrument_id = @id AND timeframe = '1D' AND ts >= @from AND ts < @to),
        m AS (
            INSERT INTO market.bars_agg AS a (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete)
            SELECT @id, '1M', bucket, first(open, ts), max(high), min(low), last(close, ts), sum(volume)::bigint, sum(bars)::integer, bool_and(complete)
            FROM (SELECT *, (date_trunc('month', (ts AT TIME ZONE 'UTC') + interval '330 minutes') - interval '330 minutes') AT TIME ZONE 'UTC' AS bucket FROM d) x
            GROUP BY bucket
            {Upsert}
            RETURNING timeframe)
        SELECT timeframe, count(*) FROM m GROUP BY timeframe
        """;

    // Brings the coverage up to date, stores what the aggregates hold for the instrument and clears the changed range - unless it was widened meanwhile.
    private const string Finish =
        """
        UPDATE market.bar_coverage SET
            aggregates = (
                SELECT COALESCE(jsonb_object_agg(timeframe, jsonb_build_object('bars', n, 'last', last)), '{}'::jsonb)
                FROM (SELECT timeframe, count(*) AS n, extract(epoch FROM max(ts))::bigint AS last
                      FROM market.bars_agg WHERE instrument_id = @id GROUP BY timeframe) t),
            aggregates_built_at = @now, dirty_from = NULL, dirty_to = NULL
        WHERE instrument_id = @id AND dirty_from = @dirtyFrom AND dirty_to = @dirtyTo
        """;

    public async ValueTask<int> MarkAllDirtyAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE market.bar_coverage SET dirty_from = first_bar, dirty_to = last_bar");
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<bool> MarkDirtyAsync(Guid instrumentId, DateTimeOffset from, CancellationToken cancellationToken)
    {
        // The window runs from the first minute of the day to the instrument's last bar; a range already marked is only widened.
        await using var command = dataSource.CreateCommand(
            """
            UPDATE market.bar_coverage SET
                dirty_from = LEAST(COALESCE(dirty_from, GREATEST(@from, first_bar)), GREATEST(@from, first_bar)),
                dirty_to = GREATEST(COALESCE(dirty_to, last_bar), last_bar)
            WHERE instrument_id = @id
            """);
        command.Parameters.AddWithValue("id", instrumentId);
        command.Parameters.AddWithValue("from", from);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async ValueTask<IReadOnlyList<DirtyInstrument>> ListDirtyAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT c.instrument_id, i.canonical_symbol, c.dirty_from, c.dirty_to
            FROM market.bar_coverage c
            JOIN market.instruments i ON i.id = c.instrument_id
            WHERE c.dirty_from IS NOT NULL
            ORDER BY i.canonical_symbol
            """);
        var list = new List<DirtyInstrument>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new DirtyInstrument(reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return list;
    }

    public async ValueTask<AggregatesBuilt> BuildAsync(DirtyInstrument target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var dayFrom = IstBuckets.DayStart(target.From);
        var dayTo = IstBuckets.DayStart(target.To).AddDays(1);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await Count(connection, transaction, IntradayAndDay, target.InstrumentId, dayFrom, dayTo, counts, cancellationToken);
        await Count(connection, transaction, Weeks, target.InstrumentId, IstBuckets.WeekStart(dayFrom), IstBuckets.WeekStart(dayTo.AddDays(-1)).AddDays(7), counts, cancellationToken);
        await Count(connection, transaction, Months, target.InstrumentId, IstBuckets.MonthStart(dayFrom), IstBuckets.NextMonthStart(dayTo.AddDays(-1)), counts, cancellationToken);

        var now = clock.GetUtcNow();
        await using (var coverage = new NpgsqlCommand(BarCoverageSql.Refresh, connection, transaction))
        {
            coverage.Parameters.AddWithValue("id", target.InstrumentId);
            coverage.Parameters.AddWithValue("now", now);
            coverage.Parameters.AddWithValue("dirtyFrom", NpgsqlDbType.TimestampTz, DBNull.Value);
            coverage.Parameters.AddWithValue("dirtyTo", NpgsqlDbType.TimestampTz, DBNull.Value);
            await coverage.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var finish = new NpgsqlCommand(Finish, connection, transaction))
        {
            finish.Parameters.AddWithValue("id", target.InstrumentId);
            finish.Parameters.AddWithValue("now", now);
            finish.Parameters.AddWithValue("dirtyFrom", target.From);
            finish.Parameters.AddWithValue("dirtyTo", target.To);
            await finish.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new AggregatesBuilt(counts);
    }

    private static async Task Count(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Guid instrumentId,
        DateTimeOffset from,
        DateTimeOffset to,
        Dictionary<string, long> counts,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 900 };
        command.Parameters.AddWithValue("id", instrumentId);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetString(0)] = counts.GetValueOrDefault(reader.GetString(0)) + reader.GetInt64(1);
        }
    }

    private static string BuildIntradayAndDay()
    {
        var selects = new List<string>();
        foreach (var timeframe in Timeframes.Intraday)
        {
            var minutes = timeframe.Minutes();
            selects.Add(
                $"""
                SELECT @id, '{timeframe.ToCode()}', g.bucket, g.o, g.h, g.l, g.c, g.v, g.n::integer,
                       g.n = (CASE WHEN x.m0 < {SessionClock.CloseMinute} THEN LEAST({minutes}, {SessionClock.CloseMinute} - x.m0) ELSE {minutes} END)
                FROM (
                    SELECT date_bin(interval '{minutes} minutes', ts, {IntradayOrigin}) AS bucket,
                           first(open, ts) AS o, max(high) AS h, min(low) AS l, last(close, ts) AS c, sum(volume)::bigint AS v, count(*) AS n
                    FROM src GROUP BY date_bin(interval '{minutes} minutes', ts, {IntradayOrigin})) g
                CROSS JOIN LATERAL (SELECT ((extract(epoch FROM g.bucket)::bigint + 19800) % 86400) / 60 AS m0) x
                """);
        }

        selects.Add(
            $"""
            SELECT @id, '1D', bucket, o, h, l, c, v, n::integer, regular = {SessionClock.RegularBars} OR (regular = 0 AND n = span)
            FROM (
                SELECT bucket, first(open, ts) AS o, max(high) AS h, min(low) AS l, last(close, ts) AS c, sum(volume)::bigint AS v, count(*) AS n,
                       count(*) FILTER (WHERE ts >= bucket + interval '{SessionClock.OpenMinute} minutes' AND ts < bucket + interval '{SessionClock.CloseMinute} minutes') AS regular,
                       (extract(epoch FROM max(ts))::bigint - extract(epoch FROM min(ts))::bigint) / 60 + 1 AS span
                FROM (SELECT *, date_bin(interval '1 day', ts, {DayOrigin}) AS bucket FROM src) b
                GROUP BY bucket) g
            """);

        return
            $"""
            WITH src AS MATERIALIZED (
                SELECT ts, open, high, low, close, volume FROM market.bars_1m
                WHERE instrument_id = @id AND ts >= @from AND ts < @to),
            w AS (
                INSERT INTO market.bars_agg AS a (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete)
                {string.Join("\nUNION ALL\n", selects)}
                {Upsert}
                RETURNING timeframe)
            SELECT timeframe, count(*) FROM w GROUP BY timeframe
            """;
    }
}
