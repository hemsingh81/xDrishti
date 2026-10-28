using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Research;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary>
/// The live capture record (<c>trading.live_capture_runs</c>, <c>live_fills</c>, <c>live_fill_charges</c>, <c>live_orders</c>, <c>live_ticket_outcomes</c>, spec P11-01, ADR 0149): insert-only for the runtime role. Writes use <c>ON CONFLICT DO NOTHING</c> against
/// the unique keys, so a row seen again is not stored again and the count of new rows is exact. The tables check the same rules again.
/// </summary>
internal sealed class LiveCaptureStore(NpgsqlDataSource dataSource) : ILiveCaptureStore
{
    private static string SideText(Side side) => side == Side.Buy ? "buy" : "sell";

    private static Side ParseSide(string text) => text == "buy" ? Side.Buy : Side.Sell;

    public async ValueTask<int> InsertFillsAsync(IReadOnlyList<LiveFill> fills, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fills);
        var added = 0;
        await using var batch = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var f in fills)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO trading.live_fills (id, account_id, broker_trade_id, broker_order_id, symbol, security_id, side, product, qty, price, trade_date, trade_time, source)
                VALUES (@id, @account, @trade, @order, @symbol, NULL, @side, @product, @qty, @price, @date, @time, @source)
                ON CONFLICT (account_id, broker_trade_id) DO NOTHING
                """, batch);
            command.Parameters.AddWithValue("id", f.Id);
            command.Parameters.AddWithValue("account", f.AccountId);
            command.Parameters.AddWithValue("trade", f.BrokerTradeId);
            command.Parameters.Add(new NpgsqlParameter("order", NpgsqlDbType.Varchar) { Value = (object?)f.BrokerOrderId ?? DBNull.Value });
            command.Parameters.AddWithValue("symbol", f.Symbol);
            command.Parameters.AddWithValue("side", SideText(f.Side));
            command.Parameters.AddWithValue("product", f.Product);
            command.Parameters.AddWithValue("qty", f.Qty);
            command.Parameters.Add(new NpgsqlParameter("price", NpgsqlDbType.Numeric) { Value = f.Price });
            command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = f.Date });
            command.Parameters.Add(new NpgsqlParameter("time", NpgsqlDbType.Time) { Value = (object?)f.Time ?? DBNull.Value });
            command.Parameters.AddWithValue("source", source);
            added += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return added;
    }

    public async ValueTask<int> InsertOrdersAsync(IReadOnlyList<LiveOrder> orders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orders);
        var added = 0;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var o in orders)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO trading.live_orders (account_id, broker_order_id, symbol, side, product, order_type, validity, qty, price, trigger_price, status, filled_qty, avg_traded_price, create_time, exchange_time, update_time, leg_name, algo_id, reject_text, trade_date)
                VALUES (@account, @order, @symbol, @side, @product, @type, @validity, @qty, @price, @trigger, @status, @filled, @avg, @created, @exchange, @updated, @leg, @algo, @reject, @date)
                ON CONFLICT (account_id, broker_order_id, status, filled_qty) DO NOTHING
                """, connection);
            command.Parameters.AddWithValue("account", o.AccountId);
            command.Parameters.AddWithValue("order", o.BrokerOrderId);
            command.Parameters.AddWithValue("symbol", o.Symbol);
            command.Parameters.AddWithValue("side", SideText(o.Side));
            command.Parameters.AddWithValue("product", o.Product);
            command.Parameters.AddWithValue("type", o.OrderType);
            command.Parameters.AddWithValue("validity", o.Validity);
            command.Parameters.AddWithValue("qty", o.Qty);
            command.Parameters.Add(Decimal("price", o.Price));
            command.Parameters.Add(Decimal("trigger", o.TriggerPrice));
            command.Parameters.AddWithValue("status", o.Status);
            command.Parameters.AddWithValue("filled", o.FilledQty);
            command.Parameters.Add(Decimal("avg", o.AvgTradedPrice));
            command.Parameters.Add(new NpgsqlParameter("created", NpgsqlDbType.TimestampTz) { Value = o.CreateTime.UtcDateTime });
            command.Parameters.Add(Moment("exchange", o.ExchangeTime));
            command.Parameters.Add(Moment("updated", o.UpdateTime));
            command.Parameters.Add(Text("leg", o.LegName));
            command.Parameters.Add(Text("algo", o.AlgoId));
            command.Parameters.Add(Text("reject", o.RejectText));
            command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = o.Date });
            added += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return added;
    }

    public async ValueTask InsertRunAsync(NewCaptureRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO trading.live_capture_runs (account_id, kind, for_date, started_at, finished_at, outcome, fills_seen, fills_new, orders_seen, orders_new)
            VALUES (@account, @kind, @date, @started, @finished, @outcome, @fs, @fn, @os, @on)
            """);
        command.Parameters.AddWithValue("account", run.AccountId);
        command.Parameters.AddWithValue("kind", run.Kind);
        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = run.ForDate });
        command.Parameters.Add(new NpgsqlParameter("started", NpgsqlDbType.TimestampTz) { Value = run.StartedAt.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("finished", NpgsqlDbType.TimestampTz) { Value = run.FinishedAt.UtcDateTime });
        command.Parameters.AddWithValue("outcome", run.Outcome);
        command.Parameters.AddWithValue("fs", run.FillsSeen);
        command.Parameters.AddWithValue("fn", run.FillsNew);
        command.Parameters.AddWithValue("os", run.OrdersSeen);
        command.Parameters.AddWithValue("on", run.OrdersNew);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<int> InsertChargesAsync(IReadOnlyList<(Guid FillId, decimal Charges)> charges, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(charges);
        var added = 0;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var (fill, amount) in charges)
        {
            await using var command = new NpgsqlCommand("INSERT INTO trading.live_fill_charges (fill_id, charges) VALUES (@fill, @charges) ON CONFLICT (fill_id) DO NOTHING", connection);
            command.Parameters.AddWithValue("fill", fill);
            command.Parameters.Add(new NpgsqlParameter("charges", NpgsqlDbType.Numeric) { Value = amount });
            added += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return added;
    }

    public async ValueTask<IReadOnlyList<LiveFill>> FillsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT id, account_id, broker_trade_id, broker_order_id, symbol, side, product, qty, price, trade_date, trade_time FROM trading.live_fills WHERE account_id = @account AND trade_date = @date ORDER BY trade_time NULLS LAST, id");
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date });
        var rows = new List<LiveFill>();
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            rows.Add(new LiveFill(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), ParseSide(r.GetString(5)), r.GetString(6), r.GetInt64(7), r.GetDecimal(8), r.GetFieldValue<DateOnly>(9), r.IsDBNull(10) ? null : r.GetFieldValue<TimeOnly>(10)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyDictionary<Guid, decimal>> ChargesAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT c.fill_id, c.charges FROM trading.live_fill_charges c JOIN trading.live_fills f ON f.id = c.fill_id WHERE f.account_id = @account AND f.trade_date = @date");
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date });
        var rows = new Dictionary<Guid, decimal>();
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            rows[r.GetGuid(0)] = r.GetDecimal(1);
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<LiveOrder>> LatestOrdersAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT DISTINCT ON (broker_order_id) account_id, broker_order_id, symbol, side, product, order_type, validity, qty, price, trigger_price, status, filled_qty, avg_traded_price, create_time, exchange_time, update_time, leg_name, algo_id, reject_text, trade_date
            FROM trading.live_orders WHERE account_id = @account AND trade_date = @date ORDER BY broker_order_id, id DESC
            """);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date });
        var rows = new List<LiveOrder>();
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            rows.Add(new LiveOrder(
                r.GetGuid(0), r.GetString(1), r.GetString(2), ParseSide(r.GetString(3)), r.GetString(4), r.GetString(5), r.GetString(6), r.GetInt64(7), NullableDecimal(r, 8), NullableDecimal(r, 9), r.GetString(10), r.GetInt64(11), NullableDecimal(r, 12),
                Utc(r.GetDateTime(13)), r.IsDBNull(14) ? null : Utc(r.GetDateTime(14)), r.IsDBNull(15) ? null : Utc(r.GetDateTime(15)), NullableText(r, 16), NullableText(r, 17), NullableText(r, 18), r.GetFieldValue<DateOnly>(19)));
        }

        return [.. rows.OrderBy(o => o.CreateTime).ThenBy(o => o.BrokerOrderId, StringComparer.Ordinal)];
    }

    public async ValueTask<IReadOnlyList<CaptureRun>> RunsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken) =>
        [.. (await RunDetailsAsync(accountId, date, cancellationToken)).Select(r => new CaptureRun(r.Kind, r.StartedAt, r.FinishedAt, r.Outcome))];

    public async ValueTask<IReadOnlyList<StoredCaptureRun>> RunDetailsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT kind, started_at, finished_at, outcome, fills_new, orders_new FROM trading.live_capture_runs WHERE account_id = @account AND for_date = @date ORDER BY id");
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date });
        var rows = new List<StoredCaptureRun>();
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            rows.Add(new StoredCaptureRun(r.GetString(0), Utc(r.GetDateTime(1)), Utc(r.GetDateTime(2)), r.GetString(3), r.GetInt32(4), r.GetInt32(5)));
        }

        return rows;
    }

    public async ValueTask<int> InsertOutcomesAsync(IReadOnlyList<NewTicketOutcome> outcomes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var added = 0;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var n in outcomes)
        {
            var o = n.Outcome;
            await using var command = new NpgsqlCommand("""
                INSERT INTO trading.live_ticket_outcomes (item_id, account_id, for_date, outcome, allocated_qty, ordered_qty, entry_qty, first_entry_at, flags, missed_r, plan_grade_status, order_evidence, protection, reject_text)
                VALUES (@item, @account, @date, @outcome, @alloc, @ordered, @entry, @first, @flags, @missed, @grade, @evidence, @protection, @reject)
                ON CONFLICT (item_id, account_id) DO NOTHING
                """, connection);
            command.Parameters.AddWithValue("item", n.ItemId);
            command.Parameters.AddWithValue("account", n.AccountId);
            command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = n.ForDate });
            command.Parameters.AddWithValue("outcome", o.Outcome);
            command.Parameters.AddWithValue("alloc", n.AllocatedQty);
            command.Parameters.Add(new NpgsqlParameter("ordered", NpgsqlDbType.Bigint) { Value = (object?)o.OrderedQty ?? DBNull.Value });
            command.Parameters.AddWithValue("entry", o.EntryQty);
            command.Parameters.Add(Moment("first", o.FirstEntryAt));
            command.Parameters.Add(new NpgsqlParameter("flags", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = o.Flags.ToArray() });
            command.Parameters.Add(new NpgsqlParameter("missed", NpgsqlDbType.Double) { Value = (object?)o.MissedR ?? DBNull.Value });
            command.Parameters.AddWithValue("grade", o.PlanGradeStatus);
            command.Parameters.AddWithValue("evidence", o.OrderEvidence);
            command.Parameters.AddWithValue("protection", o.Protection);
            command.Parameters.Add(Text("reject", o.RejectText));
            added += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return added;
    }

    private const string OutcomeColumns = "id, item_id, account_id, for_date, outcome, allocated_qty, ordered_qty, entry_qty, first_entry_at, flags, missed_r, plan_grade_status, order_evidence, protection, reject_text, written_at";

    public async ValueTask<IReadOnlyList<StoredOutcome>> OutcomesOfDateAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {OutcomeColumns} FROM trading.live_ticket_outcomes WHERE account_id = @account AND for_date = @date ORDER BY id");
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date });
        return await ReadOutcomesAsync(command, cancellationToken);
    }

    public async ValueTask<StoredOutcomePage> ListOutcomesAsync(Guid? accountId, DateOnly from, DateOnly to, int skip, int take, CancellationToken cancellationToken)
    {
        await using var count = dataSource.CreateCommand("SELECT count(*) FROM trading.live_ticket_outcomes WHERE (@account::uuid IS NULL OR account_id = @account) AND for_date BETWEEN @from AND @to");
        Bind(count, accountId, from, to);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        await using var command = dataSource.CreateCommand($"SELECT {OutcomeColumns} FROM trading.live_ticket_outcomes WHERE (@account::uuid IS NULL OR account_id = @account) AND for_date BETWEEN @from AND @to ORDER BY for_date DESC, id DESC OFFSET @skip LIMIT @take");
        Bind(command, accountId, from, to);
        command.Parameters.AddWithValue("skip", skip);
        command.Parameters.AddWithValue("take", take);
        return new StoredOutcomePage(await ReadOutcomesAsync(command, cancellationToken), total);
    }

    private static void Bind(NpgsqlCommand command, Guid? accountId, DateOnly from, DateOnly to)
    {
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = (object?)accountId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to });
    }

    private static async Task<IReadOnlyList<StoredOutcome>> ReadOutcomesAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var rows = new List<StoredOutcome>();
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var outcome = new TicketOutcome(
                r.GetString(4), r.IsDBNull(6) ? null : r.GetInt64(6), r.GetInt64(7), r.IsDBNull(8) ? null : Utc(r.GetDateTime(8)), r.GetFieldValue<string[]>(9), r.IsDBNull(10) ? null : r.GetDouble(10), r.GetString(11), r.GetString(12), r.GetString(13), NullableText(r, 14));
            rows.Add(new StoredOutcome(r.GetInt64(0), r.GetInt64(1), r.GetGuid(2), r.GetFieldValue<DateOnly>(3), r.GetInt64(5), outcome, Utc(r.GetDateTime(15))));
        }

        return rows;
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);

    private static decimal? NullableDecimal(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDecimal(i);

    private static string? NullableText(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static NpgsqlParameter Decimal(string name, decimal? value) => new(name, NpgsqlDbType.Numeric) { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter Moment(string name, DateTimeOffset? value) => new(name, NpgsqlDbType.TimestampTz) { Value = value is { } v ? v.UtcDateTime : DBNull.Value };

    private static NpgsqlParameter Text(string name, string? value) => new(name, NpgsqlDbType.Varchar) { Value = (object?)value ?? DBNull.Value };
}
