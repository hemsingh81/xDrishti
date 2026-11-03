using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Eod;

namespace XDrishti.Infrastructure.Market.Eod;

/// <summary>
/// <c>market.daily_eod</c> and <c>market.eod_files</c> by plain SQL (nothing maps them in the EF model). A day is written in one transaction with an upsert that skips an identical row, so a re-run changes nothing; there is no DELETE anywhere
/// (the runtime role has none on these tables). Rows go in as arrays through <c>unnest</c>: one round trip per day.
/// </summary>
internal sealed class EodStore(NpgsqlDataSource dataSource) : IEodStore
{
    private const string Upsert = """
        INSERT INTO market.daily_eod (trade_date, series, symbol, isin, open, high, low, close, prev_close, traded_qty, turnover, trades, delivery_qty, delivery_pct, source, file_sha256, loaded_at)
        SELECT t.trade_date, t.series, t.symbol, t.isin, t.open, t.high, t.low, t.close, t.prev_close, t.traded_qty, t.turnover, t.trades, t.delivery_qty, t.delivery_pct, 'nse_cm', @sha, now()
        FROM unnest(@trade_date, @series, @symbol, @isin, @open, @high, @low, @close, @prev_close, @traded_qty, @turnover, @trades, @delivery_qty, @delivery_pct)
             AS t(trade_date, series, symbol, isin, open, high, low, close, prev_close, traded_qty, turnover, trades, delivery_qty, delivery_pct)
        ON CONFLICT (trade_date, series, symbol) DO UPDATE SET
            isin = EXCLUDED.isin, open = EXCLUDED.open, high = EXCLUDED.high, low = EXCLUDED.low, close = EXCLUDED.close, prev_close = EXCLUDED.prev_close,
            traded_qty = EXCLUDED.traded_qty, turnover = EXCLUDED.turnover, trades = EXCLUDED.trades, delivery_qty = EXCLUDED.delivery_qty, delivery_pct = EXCLUDED.delivery_pct,
            file_sha256 = EXCLUDED.file_sha256, loaded_at = EXCLUDED.loaded_at
        WHERE (market.daily_eod.isin, market.daily_eod.open, market.daily_eod.high, market.daily_eod.low, market.daily_eod.close, market.daily_eod.prev_close, market.daily_eod.traded_qty,
               market.daily_eod.turnover, market.daily_eod.trades, market.daily_eod.delivery_qty, market.daily_eod.delivery_pct)
              IS DISTINCT FROM
              (EXCLUDED.isin, EXCLUDED.open, EXCLUDED.high, EXCLUDED.low, EXCLUDED.close, EXCLUDED.prev_close, EXCLUDED.traded_qty, EXCLUDED.turnover, EXCLUDED.trades, EXCLUDED.delivery_qty, EXCLUDED.delivery_pct)
        """;

    public async ValueTask<IReadOnlySet<DateOnly>> ListDoneDatesAsync(DateOnly from, DateOnly to, bool recheckAbsent, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT trade_date FROM market.eod_files WHERE kind = 'bhav' AND trade_date BETWEEN @from AND @to AND (NOT @recheck OR status = 'loaded')");
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        command.Parameters.AddWithValue("recheck", recheckAbsent);
        return await ReadDatesAsync(command, cancellationToken);
    }

    public async ValueTask<IReadOnlySet<DateOnly>> ListSpecialSessionDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT date FROM market.trading_days WHERE kind = 'special' AND status = 'confirmed' AND date BETWEEN @from AND @to");
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        return await ReadDatesAsync(command, cancellationToken);
    }

    private static async ValueTask<IReadOnlySet<DateOnly>> ReadDatesAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var dates = new HashSet<DateOnly>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            dates.Add(reader.GetFieldValue<DateOnly>(0));
        }

        return dates;
    }

    public async ValueTask WriteDayAsync(DateOnly date, IReadOnlyList<EodRow> rows, string bhavSha, string? deliverySha, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (rows.Count > 0)
        {
            await using var command = new NpgsqlCommand(Upsert, connection, transaction);
            command.Parameters.AddWithValue("sha", bhavSha);
            Add(command, "trade_date", NpgsqlDbType.Date, rows.Select(r => r.TradeDate).ToArray());
            Add(command, "series", NpgsqlDbType.Varchar, rows.Select(r => r.Series).ToArray());
            Add(command, "symbol", NpgsqlDbType.Varchar, rows.Select(r => r.Symbol).ToArray());
            Add(command, "isin", NpgsqlDbType.Varchar, rows.Select(r => r.Isin).ToArray());
            Add(command, "open", NpgsqlDbType.Numeric, rows.Select(r => r.Open).ToArray());
            Add(command, "high", NpgsqlDbType.Numeric, rows.Select(r => r.High).ToArray());
            Add(command, "low", NpgsqlDbType.Numeric, rows.Select(r => r.Low).ToArray());
            Add(command, "close", NpgsqlDbType.Numeric, rows.Select(r => r.Close).ToArray());
            Add(command, "prev_close", NpgsqlDbType.Numeric, rows.Select(r => r.PrevClose).ToArray());
            Add(command, "traded_qty", NpgsqlDbType.Bigint, rows.Select(r => r.TradedQty).ToArray());
            Add(command, "turnover", NpgsqlDbType.Numeric, rows.Select(r => r.Turnover).ToArray());
            Add(command, "trades", NpgsqlDbType.Bigint, rows.Select(r => r.Trades).ToArray());
            Add(command, "delivery_qty", NpgsqlDbType.Bigint, rows.Select(r => r.DeliveryQty).ToArray());
            Add(command, "delivery_pct", NpgsqlDbType.Numeric, rows.Select(r => r.DeliveryPct).ToArray());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await LedgerAsync(connection, transaction, date, "bhav", "loaded", bhavSha, rows.Count, cancellationToken);
        if (deliverySha is not null)
        {
            await LedgerAsync(connection, transaction, date, "delivery", "loaded", deliverySha, rows.Count(r => r.DeliveryQty is not null), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask MarkAbsentAsync(DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO market.eod_files (trade_date, kind, status, row_count, fetched_at) VALUES (@date, 'bhav', 'absent', 0, now()) ON CONFLICT (trade_date, kind) DO NOTHING");
        command.Parameters.AddWithValue("date", date);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<EodUniverseRow>> UniverseAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT isin, symbol, series, known_since, last_seen, in_catalog FROM market.eod_universe(@as_of) ORDER BY symbol, series");
        command.Parameters.AddWithValue("as_of", asOf);
        var rows = new List<EodUniverseRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new EodUniverseRow(reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateOnly>(3), reader.GetFieldValue<DateOnly>(4), reader.GetBoolean(5)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<DailyMinuteRow>> DailyAgainstMinuteAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT d.symbol, d.trade_date, d.traded_qty, m.volume, d.high, m.high::numeric, COALESCE(i.tick_size, 0.05)::numeric
            FROM market.daily_eod d
            JOIN market.instruments i ON i.isin = d.isin AND i.type = 'share'
            CROSS JOIN LATERAL (
                SELECT sum(b.volume)::bigint AS volume, max(b.high) AS high
                FROM market.bars_1m b
                WHERE b.instrument_id = i.id
                  AND b.ts >= (d.trade_date::timestamp AT TIME ZONE 'Asia/Kolkata')
                  AND b.ts < ((d.trade_date + 1)::timestamp AT TIME ZONE 'Asia/Kolkata')) m
            WHERE d.trade_date BETWEEN @from AND @to AND d.series = 'EQ' AND m.volume IS NOT NULL
              -- The loaded CSV minute bars are split-adjusted at source through their last bar: a session before an applied split or bonus whose ex-date is within that range is not comparable with the raw daily row.
              AND NOT EXISTS (
                  SELECT 1 FROM market.corporate_actions a
                  WHERE a.instrument_id = i.id AND a.status = 'applied' AND a.ex_date > d.trade_date
                    AND a.ex_date <= (SELECT (max(f.last_bar) AT TIME ZONE 'Asia/Kolkata')::date FROM market.csv_files f WHERE f.instrument_id = i.id AND f.undone_at IS NULL AND f.last_bar IS NOT NULL))
            ORDER BY d.trade_date, d.symbol
            LIMIT 200000
            """);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        var rows = new List<DailyMinuteRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new DailyMinuteRow(reader.GetString(0), reader.GetFieldValue<DateOnly>(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6)));
        }

        return rows;
    }

    private static void Add<T>(NpgsqlCommand command, string name, NpgsqlDbType type, T[] values) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | type) { Value = values });

    private static async ValueTask LedgerAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, DateOnly date, string kind, string status, string? sha, int rows, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO market.eod_files (trade_date, kind, status, sha256, row_count, fetched_at) VALUES (@date, @kind, @status, @sha, @rows, now())
            ON CONFLICT (trade_date, kind) DO UPDATE SET status = EXCLUDED.status, sha256 = EXCLUDED.sha256, row_count = EXCLUDED.row_count, fetched_at = EXCLUDED.fetched_at
            WHERE market.eod_files.status = 'absent' OR market.eod_files.sha256 IS DISTINCT FROM EXCLUDED.sha256
            """, connection, transaction);
        command.Parameters.AddWithValue("date", date);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("sha", (object?)sha ?? DBNull.Value);
        command.Parameters.AddWithValue("rows", rows);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
