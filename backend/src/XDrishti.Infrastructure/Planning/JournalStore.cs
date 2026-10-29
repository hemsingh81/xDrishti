using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Portfolio;
using XDrishti.Domain.Research;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The auto-journal behind <see cref="IJournalStore"/> (spec P7-14, ADR 0093): raw SQL. It only SELECTs from <c>portfolio.transactions</c> (the fills the existing read-only sync stored) and, since P11-01b (ADR 0154), from <c>trading.live_fills</c>
/// (the intraday fills the live capture stored; a delivery fill or a fill that has a stored twin is never read from there, so no fill is journaled twice), and inserts into its own three tables, which the runtime role can
/// insert into and read but not change. A second save of the same fill or entry is skipped by the unique indexes (<c>ON CONFLICT DO NOTHING</c>). Nothing here talks to a broker.
/// </summary>
internal sealed class JournalStore(NpgsqlDataSource dataSource) : IJournalStore
{
    public async ValueTask<IReadOnlyList<JournalFill>> FillsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT t.id, t.account_id, t.trading_symbol, t.side, t.trade_date, t.trade_time, t.qty, t.price, t.charges
            FROM portfolio.transactions t
            WHERE t.source = 'broker' AND t.voided_at IS NULL AND t.trade_date BETWEEN @from AND @to
              AND (t.account_id IN (SELECT s.account_id FROM accounts.trading_settings s WHERE s.enabled)
                   OR t.account_id IN (SELECT a.account_id FROM trading.plan_item_allocations a))
              -- a stored trade whose live twin is already journaled is not journaled again (live first, stored later; trg_journal_fills_single_source would refuse it and fail the whole save)
              AND NOT EXISTS (SELECT 1 FROM trading.live_fills l JOIN trading.journal_fills j ON j.live_fill_id = l.id
                              WHERE l.account_id = t.account_id AND t.external_id = 't:' || to_char(l.trade_date, 'YYYYMMDD') || ':' || l.broker_trade_id)
            ORDER BY t.trade_date, t.trade_time NULLS LAST, t.id
            """);
        select.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
        select.Parameters.AddWithValue("to", NpgsqlDbType.Date, to);
        var fills = new List<JournalFill>();
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                fills.Add(new JournalFill(
                    reader.GetGuid(0), reader.GetGuid(1), TradeRules.NormaliseSymbol(reader.GetString(2)), reader.GetString(3) == "buy" ? Side.Buy : Side.Sell,
                    reader.GetFieldValue<DateOnly>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<TimeOnly>(5), reader.GetInt64(6), reader.GetDecimal(7), reader.GetDecimal(8)));
            }
        }

        // Shortcut: the stored query above also reads accounts that are Trading-enabled without an allocation; the live query is allocation-only, because the live capture exists only for accounts a live ticket is allocated to.
        // The live fills (P11-01b): intraday products only (a delivery fill reaches the journal through the stored trade), never one that has a stored twin (same account, identity t:yyyyMMdd:trade id), and only for accounts with an allocation.
        await using var live = dataSource.CreateCommand("""
            SELECT l.id, l.account_id, l.symbol, l.side, l.trade_date, l.trade_time, l.qty, l.price, c.charges
            FROM trading.live_fills l
            LEFT JOIN trading.live_fill_charges c ON c.fill_id = l.id
            WHERE l.trade_date BETWEEN @from AND @to
              AND upper(l.product) <> ALL(@delivery)
              AND l.account_id IN (SELECT a.account_id FROM trading.plan_item_allocations a)
              AND NOT EXISTS (SELECT 1 FROM portfolio.transactions t WHERE t.account_id = l.account_id AND t.voided_at IS NULL
                              AND t.external_id = 't:' || to_char(l.trade_date, 'YYYYMMDD') || ':' || l.broker_trade_id)
            ORDER BY l.trade_date, l.trade_time NULLS LAST, l.id
            """);
        live.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
        live.Parameters.AddWithValue("to", NpgsqlDbType.Date, to);
        live.Parameters.AddWithValue("delivery", NpgsqlDbType.Array | NpgsqlDbType.Varchar, DayLoss.DeliveryProducts.ToArray());
        await using var liveReader = await live.ExecuteReaderAsync(cancellationToken);
        while (await liveReader.ReadAsync(cancellationToken))
        {
            fills.Add(new JournalFill(
                liveReader.GetGuid(0), liveReader.GetGuid(1), TradeRules.NormaliseSymbol(liveReader.GetString(2)), liveReader.GetString(3) == "buy" ? Side.Buy : Side.Sell,
                liveReader.GetFieldValue<DateOnly>(4), liveReader.IsDBNull(5) ? null : liveReader.GetFieldValue<TimeOnly>(5), liveReader.GetInt64(6), liveReader.GetDecimal(7),
                liveReader.IsDBNull(8) ? 0m : liveReader.GetDecimal(8), Live: true, ChargesKnown: !liveReader.IsDBNull(8)));
        }

        return fills;
    }

    public async ValueTask<JournalSaved> SaveAsync(IReadOnlyList<NewJournalLink> links, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(links);
        if (links.Count == 0)
        {
            return new JournalSaved(0, 0);
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var entryIds = new Dictionary<(long Item, Guid Account), long>();
        int newEntries = 0, newFills = 0;
        foreach (var group in links.Where(l => l.Link.ItemId is not null).GroupBy(l => (Item: l.Link.ItemId!.Value, Account: l.Link.AccountId)).OrderBy(g => g.Key.Item).ThenBy(g => g.Key.Account))
        {
            var first = group.First();
            await using var insert = new NpgsqlCommand("""
                INSERT INTO trading.journal_entries (plan_id, item_id, account_id, for_date, created_at, uncertain_reason) VALUES (@plan, @item, @account, @date, @at, @uncertain)
                ON CONFLICT (item_id, account_id) DO NOTHING RETURNING id
                """, connection, transaction);
            insert.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, first.PlanId);
            insert.Parameters.AddWithValue("item", NpgsqlDbType.Bigint, group.Key.Item);
            insert.Parameters.AddWithValue("account", NpgsqlDbType.Uuid, group.Key.Account);
            insert.Parameters.AddWithValue("date", NpgsqlDbType.Date, first.ForDate);
            insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, at);
            insert.Parameters.AddWithValue("uncertain", NpgsqlDbType.Varchar, (object?)group.Select(l => l.Uncertain).FirstOrDefault(u => u is not null) ?? DBNull.Value);
            if (await insert.ExecuteScalarAsync(cancellationToken) is long created)
            {
                entryIds[group.Key] = created;
                newEntries++;
                continue;
            }

            await using var find = new NpgsqlCommand("SELECT id FROM trading.journal_entries WHERE item_id = @item AND account_id = @account", connection, transaction);
            find.Parameters.AddWithValue("item", NpgsqlDbType.Bigint, group.Key.Item);
            find.Parameters.AddWithValue("account", NpgsqlDbType.Uuid, group.Key.Account);
            entryIds[group.Key] = (long)(await find.ExecuteScalarAsync(cancellationToken))!;
        }

        foreach (var l in links.OrderBy(l => l.Link.Fill.Date).ThenBy(l => l.Link.Fill.Time ?? TimeOnly.MaxValue).ThenBy(l => l.Link.Fill.TransactionId))
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO trading.journal_fills (transaction_id, live_fill_id, entry_id, role, qty, reason, for_date, journaled_at) VALUES (@tx, @live, @entry, @role, @qty, @reason, @date, @at)
                ON CONFLICT DO NOTHING
                """, connection, transaction);
            var link = l.Link;
            insert.Parameters.AddWithValue("tx", NpgsqlDbType.Uuid, link.Fill.Live ? DBNull.Value : link.Fill.TransactionId);
            insert.Parameters.AddWithValue("live", NpgsqlDbType.Uuid, link.Fill.Live ? link.Fill.TransactionId : DBNull.Value);
            insert.Parameters.AddWithValue("entry", NpgsqlDbType.Bigint, link.ItemId is { } item ? (object)entryIds[(item, link.AccountId)] : DBNull.Value);
            insert.Parameters.AddWithValue("role", NpgsqlDbType.Varchar, link.Role.Code());
            insert.Parameters.AddWithValue("qty", NpgsqlDbType.Bigint, link.Qty);
            insert.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, link.Reason.Length <= 300 ? link.Reason : link.Reason[..300]);
            insert.Parameters.AddWithValue("date", NpgsqlDbType.Date, l.ForDate);
            insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, at);
            newFills += await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new JournalSaved(newEntries, newFills);
    }

    public async ValueTask<IReadOnlyDictionary<(Guid Account, string Symbol, DateOnly Before), long>> PositionsBeforeAsync(IReadOnlyList<(Guid Account, string Symbol, DateOnly Before)> keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var result = new Dictionary<(Guid, string, DateOnly), long>();
        if (keys.Count == 0)
        {
            return result;
        }

        // Shortcut: the history the app holds. Shares bought before the first stored trade are invisible here.
        await using var select = dataSource.CreateCommand("""
            SELECT k.a, k.s, k.d, COALESCE(SUM(CASE t.side WHEN 'buy' THEN t.qty ELSE -t.qty END), 0)::bigint
            FROM unnest(@accounts, @symbols, @dates) AS k(a, s, d)
            LEFT JOIN portfolio.transactions t ON t.account_id = k.a AND t.trading_symbol = k.s AND t.voided_at IS NULL AND t.trade_date < k.d
            GROUP BY k.a, k.s, k.d
            """);
        select.Parameters.AddWithValue("accounts", NpgsqlDbType.Array | NpgsqlDbType.Uuid, keys.Select(k => k.Account).ToArray());
        select.Parameters.AddWithValue("symbols", NpgsqlDbType.Array | NpgsqlDbType.Varchar, keys.Select(k => k.Symbol).ToArray());
        select.Parameters.AddWithValue("dates", NpgsqlDbType.Array | NpgsqlDbType.Date, keys.Select(k => k.Before).ToArray());
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[(reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateOnly>(2))] = reader.GetInt64(3);
        }

        return result;
    }

    public ValueTask<IReadOnlyList<StoredEntry>> OpenEntriesAsync(int limit, CancellationToken cancellationToken) =>
        ReadEntriesAsync("e.id IN (SELECT x.id FROM trading.journal_entries x WHERE NOT EXISTS (SELECT 1 FROM trading.fill_grades g WHERE g.entry_id = x.id) ORDER BY x.for_date DESC, x.id DESC LIMIT @limit)", cmd => cmd.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit), cancellationToken);

    public ValueTask<IReadOnlyList<StoredEntry>> EntriesForPlansAsync(IReadOnlyList<long> planIds, CancellationToken cancellationToken) =>
        planIds.Count == 0
            ? ValueTask.FromResult<IReadOnlyList<StoredEntry>>([])
            : ReadEntriesAsync("e.plan_id = ANY(@plans)", cmd => cmd.Parameters.AddWithValue("plans", NpgsqlDbType.Array | NpgsqlDbType.Bigint, planIds.ToArray()), cancellationToken);

    public ValueTask<IReadOnlyList<StoredEntry>> EntriesAsync(long planId, CancellationToken cancellationToken) =>
        ReadEntriesAsync("e.plan_id = @plan", cmd => cmd.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, planId), cancellationToken);

    public async ValueTask<IReadOnlyList<StoredFill>> DiscretionaryAsync(DateOnly forDate, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"""
            SELECT {FillColumns}
            FROM trading.journal_fills f {FillJoins}
            WHERE f.role = 'discretionary' AND f.for_date = @date AND {FillLive}
            ORDER BY {FillDate}, {FillTime} NULLS LAST, f.id
            """);
        select.Parameters.AddWithValue("date", NpgsqlDbType.Date, forDate);
        var fills = new List<StoredFill>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            fills.Add(ReadFill(reader, 0));
        }

        return fills;
    }

    public async ValueTask<int> InsertGradesAsync(IReadOnlyList<NewFillGrade> grades, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grades);
        if (grades.Count == 0)
        {
            return 0;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var written = 0;
        foreach (var g in grades.OrderBy(g => g.EntryId))
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO trading.fill_grades (entry_id, status, qty, entry_price, exit_price, plan_entry_price, plan_stop_price, plan_r, slippage_price, slippage_r, gross_pnl, charges, r, basis, detail, graded_at)
                VALUES (@entry, @status, @qty, @ep, @xp, @pe, @ps, @pr, @sp, @sr, @gross, @charges, @r, @basis, @detail, @at)
                ON CONFLICT (entry_id) DO NOTHING
                """, connection, transaction);
            var f = g.Grade;
            var p = insert.Parameters;
            p.AddWithValue("entry", NpgsqlDbType.Bigint, g.EntryId);
            p.AddWithValue("status", NpgsqlDbType.Varchar, f.Status);
            p.AddWithValue("qty", NpgsqlDbType.Bigint, f.Qty);
            p.AddWithValue("ep", NpgsqlDbType.Double, f.EntryPrice);
            p.AddWithValue("xp", NpgsqlDbType.Double, f.ExitPrice);
            p.AddWithValue("pe", NpgsqlDbType.Double, (object?)f.PlanEntryPrice ?? DBNull.Value);
            p.AddWithValue("ps", NpgsqlDbType.Double, (object?)f.PlanStopPrice ?? DBNull.Value);
            p.AddWithValue("pr", NpgsqlDbType.Double, (object?)f.PlanR ?? DBNull.Value);
            p.AddWithValue("sp", NpgsqlDbType.Double, (object?)f.SlippagePrice ?? DBNull.Value);
            p.AddWithValue("sr", NpgsqlDbType.Double, (object?)f.SlippageR ?? DBNull.Value);
            p.AddWithValue("gross", NpgsqlDbType.Numeric, f.GrossPnl);
            p.AddWithValue("charges", NpgsqlDbType.Numeric, f.Charges);
            p.AddWithValue("r", NpgsqlDbType.Double, (object?)f.R ?? DBNull.Value);
            p.AddWithValue("basis", NpgsqlDbType.Varchar, FillGrade.Basis);
            p.AddWithValue("detail", NpgsqlDbType.Varchar, f.Detail.Length <= 500 ? f.Detail : f.Detail[..499] + "…");
            p.AddWithValue("at", NpgsqlDbType.TimestampTz, g.GradedAt);
            written += await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return written;
    }

    // Shortcut: voided fills are left out of every read (a trade voided after it was journaled keeps its row, because the tables are insert-only, but it no longer counts).
    // A link names a stored trade or a live fill (exactly one, a check constraint); both are read through the same columns. A voided stored trade drops out; a live fill is never voided.
    private const string FillJoins = "LEFT JOIN portfolio.transactions t ON t.id = f.transaction_id LEFT JOIN trading.live_fills lf ON lf.id = f.live_fill_id LEFT JOIN trading.live_fill_charges lc ON lc.fill_id = lf.id";
    private const string FillLive = "(f.live_fill_id IS NOT NULL OR t.voided_at IS NULL)";
    private const string FillDate = "COALESCE(t.trade_date, lf.trade_date)";
    private const string FillTime = "COALESCE(t.trade_time, lf.trade_time)";
    private const string FillColumns = "COALESCE(t.id, lf.id), COALESCE(t.account_id, lf.account_id), COALESCE(t.trading_symbol, lf.symbol), COALESCE(t.side, lf.side), COALESCE(t.trade_date, lf.trade_date), COALESCE(t.trade_time, lf.trade_time), COALESCE(t.qty, lf.qty), COALESCE(t.price, lf.price), COALESCE(t.charges, lc.charges, 0), f.role, f.qty, f.reason, (f.live_fill_id IS NOT NULL), (f.live_fill_id IS NULL OR lc.fill_id IS NOT NULL)";

    private static StoredFill ReadFill(NpgsqlDataReader r, int o) =>
        new(r.GetGuid(o), r.GetGuid(o + 1), TradeRules.NormaliseSymbol(r.GetString(o + 2)), r.GetString(o + 3), r.GetFieldValue<DateOnly>(o + 4), r.IsDBNull(o + 5) ? null : r.GetFieldValue<TimeOnly>(o + 5),
            r.GetInt64(o + 6), r.GetDecimal(o + 7), r.GetDecimal(o + 8), r.GetString(o + 9), r.GetInt64(o + 10), r.GetString(o + 11), r.GetBoolean(o + 12), r.GetBoolean(o + 13));

    private async ValueTask<IReadOnlyList<StoredEntry>> ReadEntriesAsync(string where, Action<NpgsqlCommand> bind, CancellationToken cancellationToken)
    {
        var heads = new List<(long Id, long PlanId, long ItemId, Guid Account, DateOnly Date, string? Uncertain)>();
        var fills = new Dictionary<long, List<StoredFill>>();
        await using (var select = dataSource.CreateCommand($"""
            SELECT e.id, e.plan_id, e.item_id, e.account_id, e.for_date, e.uncertain_reason, {FillColumns}
            FROM trading.journal_entries e
            LEFT JOIN trading.journal_fills f ON f.entry_id = e.id AND (f.live_fill_id IS NOT NULL OR EXISTS (SELECT 1 FROM portfolio.transactions v WHERE v.id = f.transaction_id AND v.voided_at IS NULL))
            LEFT JOIN portfolio.transactions t ON t.id = f.transaction_id
            LEFT JOIN trading.live_fills lf ON lf.id = f.live_fill_id
            LEFT JOIN trading.live_fill_charges lc ON lc.fill_id = lf.id
            WHERE {where}
            ORDER BY e.for_date, e.id, COALESCE(t.trade_date, lf.trade_date), COALESCE(t.trade_time, lf.trade_time) NULLS LAST, f.id
            """))
        {
            bind(select);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                if (heads.Count == 0 || heads[^1].Id != id)
                {
                    heads.Add((id, reader.GetInt64(1), reader.GetInt64(2), reader.GetGuid(3), reader.GetFieldValue<DateOnly>(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
                    fills[id] = [];
                }

                if (!reader.IsDBNull(6))
                {
                    fills[id].Add(ReadFill(reader, 6));
                }
            }
        }

        var grades = new Dictionary<long, FillGrade>();
        if (heads.Count > 0)
        {
            await using var select = dataSource.CreateCommand("""
                SELECT entry_id, status, qty, entry_price, exit_price, plan_entry_price, plan_stop_price, plan_r, slippage_price, slippage_r, gross_pnl, charges, r, detail
                FROM trading.fill_grades WHERE entry_id = ANY(@ids)
                """);
            select.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, heads.Select(h => h.Id).ToArray());
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                double? D(int i) => reader.IsDBNull(i) ? null : reader.GetDouble(i);
                grades[reader.GetInt64(0)] = new FillGrade(reader.GetString(1), reader.GetInt64(2), reader.GetDouble(3), reader.GetDouble(4), D(5), D(6), D(7), D(8), D(9), reader.GetDecimal(10), reader.GetDecimal(11), D(12), reader.GetString(13));
            }
        }

        return [.. heads.Select(h => new StoredEntry(h.Id, h.PlanId, h.ItemId, h.Account, h.Date, fills[h.Id], grades.GetValueOrDefault(h.Id), h.Uncertain))];
    }
}
