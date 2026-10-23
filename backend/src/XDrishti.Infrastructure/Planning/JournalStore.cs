using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Portfolio;
using XDrishti.Domain.Research;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The auto-journal behind <see cref="IJournalStore"/> (spec P7-14, ADR 0093): raw SQL. It only SELECTs from <c>portfolio.transactions</c> (the fills the existing read-only sync stored) and inserts into its own three tables, which the runtime role can
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
            ORDER BY t.trade_date, t.trade_time NULLS LAST, t.id
            """);
        select.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
        select.Parameters.AddWithValue("to", NpgsqlDbType.Date, to);
        var fills = new List<JournalFill>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            fills.Add(new JournalFill(
                reader.GetGuid(0), reader.GetGuid(1), TradeRules.NormaliseSymbol(reader.GetString(2)), reader.GetString(3) == "buy" ? Side.Buy : Side.Sell,
                reader.GetFieldValue<DateOnly>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<TimeOnly>(5), reader.GetInt64(6), reader.GetDecimal(7), reader.GetDecimal(8)));
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
                INSERT INTO trading.journal_entries (plan_id, item_id, account_id, for_date, created_at) VALUES (@plan, @item, @account, @date, @at)
                ON CONFLICT (item_id, account_id) DO NOTHING RETURNING id
                """, connection, transaction);
            insert.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, first.PlanId);
            insert.Parameters.AddWithValue("item", NpgsqlDbType.Bigint, group.Key.Item);
            insert.Parameters.AddWithValue("account", NpgsqlDbType.Uuid, group.Key.Account);
            insert.Parameters.AddWithValue("date", NpgsqlDbType.Date, first.ForDate);
            insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, at);
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
                INSERT INTO trading.journal_fills (transaction_id, entry_id, role, qty, reason, for_date, journaled_at) VALUES (@tx, @entry, @role, @qty, @reason, @date, @at)
                ON CONFLICT (transaction_id) DO NOTHING
                """, connection, transaction);
            var link = l.Link;
            insert.Parameters.AddWithValue("tx", NpgsqlDbType.Uuid, link.Fill.TransactionId);
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

    public ValueTask<IReadOnlyList<StoredEntry>> OpenEntriesAsync(int limit, CancellationToken cancellationToken) =>
        ReadEntriesAsync("e.id IN (SELECT x.id FROM trading.journal_entries x WHERE NOT EXISTS (SELECT 1 FROM trading.fill_grades g WHERE g.entry_id = x.id) ORDER BY x.for_date, x.id LIMIT @limit)", cmd => cmd.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit), cancellationToken);

    public ValueTask<IReadOnlyList<StoredEntry>> EntriesAsync(long planId, CancellationToken cancellationToken) =>
        ReadEntriesAsync("e.plan_id = @plan", cmd => cmd.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, planId), cancellationToken);

    public async ValueTask<IReadOnlyList<StoredFill>> DiscretionaryAsync(DateOnly forDate, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"""
            SELECT {FillColumns}
            FROM trading.journal_fills f JOIN portfolio.transactions t ON t.id = f.transaction_id
            WHERE f.role = 'discretionary' AND f.for_date = @date
            ORDER BY t.trade_date, t.trade_time NULLS LAST, f.id
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

    private const string FillColumns = "t.id, t.account_id, t.trading_symbol, t.side, t.trade_date, t.trade_time, t.qty, t.price, t.charges, f.role, f.qty, f.reason";

    private static StoredFill ReadFill(NpgsqlDataReader r, int o) =>
        new(r.GetGuid(o), r.GetGuid(o + 1), TradeRules.NormaliseSymbol(r.GetString(o + 2)), r.GetString(o + 3), r.GetFieldValue<DateOnly>(o + 4), r.IsDBNull(o + 5) ? null : r.GetFieldValue<TimeOnly>(o + 5),
            r.GetInt64(o + 6), r.GetDecimal(o + 7), r.GetDecimal(o + 8), r.GetString(o + 9), r.GetInt64(o + 10), r.GetString(o + 11));

    private async ValueTask<IReadOnlyList<StoredEntry>> ReadEntriesAsync(string where, Action<NpgsqlCommand> bind, CancellationToken cancellationToken)
    {
        var heads = new List<(long Id, long PlanId, long ItemId, Guid Account, DateOnly Date)>();
        var fills = new Dictionary<long, List<StoredFill>>();
        await using (var select = dataSource.CreateCommand($"""
            SELECT e.id, e.plan_id, e.item_id, e.account_id, e.for_date, {FillColumns}
            FROM trading.journal_entries e
            LEFT JOIN trading.journal_fills f ON f.entry_id = e.id
            LEFT JOIN portfolio.transactions t ON t.id = f.transaction_id
            WHERE {where}
            ORDER BY e.for_date, e.id, t.trade_date, t.trade_time NULLS LAST, f.id
            """))
        {
            bind(select);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                if (heads.Count == 0 || heads[^1].Id != id)
                {
                    heads.Add((id, reader.GetInt64(1), reader.GetInt64(2), reader.GetGuid(3), reader.GetFieldValue<DateOnly>(4)));
                    fills[id] = [];
                }

                if (!reader.IsDBNull(5))
                {
                    fills[id].Add(ReadFill(reader, 5));
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

        return [.. heads.Select(h => new StoredEntry(h.Id, h.PlanId, h.ItemId, h.Account, h.Date, fills[h.Id], grades.GetValueOrDefault(h.Id)))];
    }
}
