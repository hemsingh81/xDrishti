using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Paper;
using XDrishti.Domain.Paper;

namespace XDrishti.Infrastructure.Paper;

/// <summary>
/// The paper record behind <see cref="IPaperStore"/> (spec P10-01, ADR 0116): raw SQL, select and insert only (the runtime role has no more). Every insert is <c>ON CONFLICT DO NOTHING</c> on the unique keys, so writing twice stores once
/// and a stored entry is never changed. Nothing here talks to a broker.
/// </summary>
internal sealed class PaperStore(NpgsqlDataSource dataSource) : IPaperStore
{
    private const string EpochColumns = "id, started_on, started_at, kind, reason, actor";

    public async ValueTask<EpochRow?> CurrentEpochAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {EpochColumns} FROM paper.epochs ORDER BY id DESC LIMIT 1");
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new EpochRow(reader.GetInt64(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3), reader.GetString(4), reader.GetString(5))
            : null;
    }

    public async ValueTask<EpochRow> EnsureFirstEpochAsync(DateOnly startedOn, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (await CurrentEpochAsync(cancellationToken) is { } current)
        {
            return current;
        }

        await using var insert = dataSource.CreateCommand("""
            INSERT INTO paper.epochs (started_on, started_at, kind, reason, actor)
            SELECT @on, @at, 'first', 'the default epoch, started with the first capture', 'system:paper-capture'
            WHERE NOT EXISTS (SELECT 1 FROM paper.epochs)
            ON CONFLICT DO NOTHING
            """);
        insert.Parameters.AddWithValue("on", NpgsqlDbType.Date, startedOn);
        insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, at);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        return (await CurrentEpochAsync(cancellationToken))!;
    }

    public async ValueTask<IReadOnlyDictionary<(long ItemId, Guid AccountId), StoredPair>> PairsAsync(IReadOnlyList<long> planIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(planIds);
        var result = new Dictionary<(long, Guid), StoredPair>();
        if (planIds.Count == 0)
        {
            return result;
        }

        await using (var select = dataSource.CreateCommand("""
            SELECT e.id, e.plan_id, e.plan_item_id, e.account_id, e.symbol, e.side, e.strategy_id, e.strategy_hash, e.cell_key, e.ticket_group, e.approval_id, e.approval_kind, e.qty_allocated, e.qty,
                   e.armed_at, e.entry_at, e.entry_price, e.entry_exact, e.stop_price, e.target_price, e.swing, e.trade_key, e.run_key, e.engine_version, (x.id IS NOT NULL)
            FROM paper.entries e LEFT JOIN paper.exits x ON x.entry_id = e.id
            WHERE e.plan_id = ANY(@plans)
            """))
        {
            select.Parameters.AddWithValue("plans", NpgsqlDbType.Array | NpgsqlDbType.Bigint, planIds.ToArray());
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var entry = new PaperEntry(
                    reader.GetInt64(1), reader.GetInt64(2), reader.GetGuid(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetInt64(10), reader.GetString(11),
                    reader.GetInt32(12), reader.GetInt32(13), reader.GetFieldValue<DateTimeOffset>(14), reader.GetFieldValue<DateTimeOffset>(15), reader.GetDecimal(16), reader.GetDecimal(17), reader.GetDecimal(18),
                    reader.IsDBNull(19) ? null : reader.GetDecimal(19), reader.GetBoolean(20), reader.GetString(21), reader.GetString(22), reader.GetString(23));
                result[(entry.PlanItemId, entry.AccountId)] = new StoredPair(reader.GetInt64(0), entry, reader.GetBoolean(24), false);
            }
        }

        await using var skips = dataSource.CreateCommand("SELECT plan_item_id, account_id FROM paper.skips WHERE plan_id = ANY(@plans)");
        skips.Parameters.AddWithValue("plans", NpgsqlDbType.Array | NpgsqlDbType.Bigint, planIds.ToArray());
        await using var skipReader = await skips.ExecuteReaderAsync(cancellationToken);
        while (await skipReader.ReadAsync(cancellationToken))
        {
            result[(skipReader.GetInt64(0), skipReader.GetGuid(1))] = new StoredPair(null, null, false, true);
        }

        return result;
    }

    public async ValueTask<SessionCoverage> CoverageAsync(CancellationToken cancellationToken)
    {
        var covered = new HashSet<DateOnly>();
        var revisit = new HashSet<DateOnly>();
        await using (var select = dataSource.CreateCommand("SELECT DISTINCT session_date FROM paper.capture_runs"))
        {
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                covered.Add(DateOnly.FromDateTime(reader.GetDateTime(0)));
            }
        }

        // A session is looked at again when its newest row is not done, or an entry of it has no exit yet (an open swing position).
        // Shortcut: a session that stays pending for good (an instrument that left every basket, a replay that is never mended) is replayed on every run and never leaves this list; there is no give-up age. The cost is bounded by the 30-session cap per run.
        await using var again = dataSource.CreateCommand("""
            SELECT session_date FROM (SELECT DISTINCT ON (session_date) session_date, outcome FROM paper.capture_runs ORDER BY session_date, id DESC) r WHERE outcome <> 'done'
            UNION
            SELECT p.for_date FROM paper.entries e JOIN trading.plans p ON p.id = e.plan_id LEFT JOIN paper.exits x ON x.entry_id = e.id WHERE x.id IS NULL
            """);
        await using var rows = await again.ExecuteReaderAsync(cancellationToken);
        while (await rows.ReadAsync(cancellationToken))
        {
            revisit.Add(DateOnly.FromDateTime(rows.GetDateTime(0)));
        }

        return new SessionCoverage(covered, revisit);
    }

    public async ValueTask<SessionWritten> WriteSessionAsync(SessionWrite write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        int entries = 0, exits = 0, skips = 0;
        foreach (var a in write.Accounts)
        {
            await using var account = new NpgsqlCommand("INSERT INTO paper.epoch_accounts (epoch_id, account_id, capital, capital_source, mode) VALUES (@epoch, @account, @capital, @source, 'paper') ON CONFLICT (epoch_id, account_id) DO NOTHING", connection, transaction);
            account.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, write.EpochId);
            account.Parameters.AddWithValue("account", NpgsqlDbType.Uuid, a.AccountId);
            account.Parameters.AddWithValue("capital", NpgsqlDbType.Numeric, a.Capital);
            account.Parameters.AddWithValue("source", NpgsqlDbType.Varchar, a.CapitalSource);
            await account.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var n in write.Entries)
        {
            var e = n.Entry;
            await using var insert = new NpgsqlCommand("""
                INSERT INTO paper.entries (plan_id, plan_item_id, account_id, epoch_id, measure_status, ticket_group, approval_id, approval_kind, symbol, side, strategy_id, strategy_hash, cell_key, trade_key, qty_allocated, qty,
                                           armed_at, entry_at, entry_price, entry_exact, stop_price, target_price, swing, run_key, engine_version, basis)
                SELECT @plan, @item, @account, @epoch, @measure, @group, @approval, @kind, @symbol, @side, @strategy, @hash, @cell, @tradekey, @alloc, @qty, @armed, @entry, @price, @exact, @stop, @target, @swing, @run, @engine, @basis
                WHERE NOT EXISTS (SELECT 1 FROM paper.skips s WHERE s.plan_item_id = @item AND s.account_id = @account)
                ON CONFLICT (plan_item_id, account_id) DO NOTHING
                RETURNING id
                """, connection, transaction);
            var p = insert.Parameters;
            p.AddWithValue("plan", NpgsqlDbType.Bigint, e.PlanId);
            p.AddWithValue("item", NpgsqlDbType.Bigint, e.PlanItemId);
            p.AddWithValue("account", NpgsqlDbType.Uuid, e.AccountId);
            p.AddWithValue("epoch", NpgsqlDbType.Bigint, write.EpochId);
            p.AddWithValue("measure", NpgsqlDbType.Varchar, n.MeasureStatus);
            p.AddWithValue("group", NpgsqlDbType.Varchar, e.TicketGroup);
            p.AddWithValue("approval", NpgsqlDbType.Bigint, e.ApprovalId);
            p.AddWithValue("kind", NpgsqlDbType.Varchar, e.ApprovalKind);
            p.AddWithValue("symbol", NpgsqlDbType.Varchar, e.Symbol);
            p.AddWithValue("side", NpgsqlDbType.Varchar, e.Side);
            p.AddWithValue("strategy", NpgsqlDbType.Varchar, e.StrategyId);
            p.AddWithValue("hash", NpgsqlDbType.Char, e.StrategyHash);
            p.AddWithValue("cell", NpgsqlDbType.Varchar, e.CellKey);
            p.AddWithValue("tradekey", NpgsqlDbType.Varchar, e.TradeKey);
            p.AddWithValue("alloc", NpgsqlDbType.Integer, e.QtyAllocated);
            p.AddWithValue("qty", NpgsqlDbType.Integer, e.Qty);
            p.AddWithValue("armed", NpgsqlDbType.TimestampTz, e.ArmedAt);
            p.AddWithValue("entry", NpgsqlDbType.TimestampTz, e.EntryAt);
            p.AddWithValue("price", NpgsqlDbType.Numeric, e.EntryPrice);
            p.AddWithValue("exact", NpgsqlDbType.Numeric, e.EntryExact);
            p.AddWithValue("stop", NpgsqlDbType.Numeric, e.StopPrice);
            p.AddWithValue("target", NpgsqlDbType.Numeric, (object?)e.TargetPrice ?? DBNull.Value);
            p.AddWithValue("swing", NpgsqlDbType.Boolean, e.Swing);
            p.AddWithValue("run", NpgsqlDbType.Char, e.RunKey);
            p.AddWithValue("engine", NpgsqlDbType.Varchar, e.EngineVersion);
            p.AddWithValue("basis", NpgsqlDbType.Varchar, PaperWording.Basis);
            if (await insert.ExecuteScalarAsync(cancellationToken) is not long id)
            {
                continue;
            }

            entries++;
            if (n.Exit is { } exit)
            {
                exits += await InsertExitAsync(connection, transaction, id, e.EntryAt, exit, cancellationToken);
            }
        }

        foreach (var x in write.Exits)
        {
            exits += await InsertExitAsync(connection, transaction, x.EntryId, x.EntryAt, x.Exit, cancellationToken);
        }

        foreach (var s in write.Skips)
        {
            await using var skip = new NpgsqlCommand("INSERT INTO paper.skips (plan_id, plan_item_id, account_id, epoch_id, code, detail) SELECT @plan, @item, @account, @epoch, @code, @detail WHERE NOT EXISTS (SELECT 1 FROM paper.entries e WHERE e.plan_item_id = @item AND e.account_id = @account) ON CONFLICT (plan_item_id, account_id) DO NOTHING", connection, transaction);
            skip.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, s.PlanId);
            skip.Parameters.AddWithValue("item", NpgsqlDbType.Bigint, s.PlanItemId);
            skip.Parameters.AddWithValue("account", NpgsqlDbType.Uuid, s.AccountId);
            skip.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, write.EpochId);
            skip.Parameters.AddWithValue("code", NpgsqlDbType.Varchar, s.Code);
            skip.Parameters.AddWithValue("detail", NpgsqlDbType.Varchar, s.Detail);
            skips += await skip.ExecuteNonQueryAsync(cancellationToken);
        }

        var r = write.Run;
        await using var run = new NpgsqlCommand("""
            INSERT INTO paper.capture_runs (session_date, started_at, finished_at, outcome, seen, entries_written, exits_written, skips_written, pending, integrity, manual, reason)
            VALUES (@session, @started, @finished, @outcome, @seen, @entries, @exits, @skips, @pending, @integrity, @manual, @reason)
            """, connection, transaction);
        run.Parameters.AddWithValue("session", NpgsqlDbType.Date, r.SessionDate);
        run.Parameters.AddWithValue("started", NpgsqlDbType.TimestampTz, r.StartedAt);
        run.Parameters.AddWithValue("finished", NpgsqlDbType.TimestampTz, r.FinishedAt < r.StartedAt ? r.StartedAt : r.FinishedAt);
        run.Parameters.AddWithValue("outcome", NpgsqlDbType.Varchar, r.Outcome);
        run.Parameters.AddWithValue("seen", NpgsqlDbType.Integer, r.Seen);
        run.Parameters.AddWithValue("entries", NpgsqlDbType.Integer, entries);
        run.Parameters.AddWithValue("exits", NpgsqlDbType.Integer, exits);
        run.Parameters.AddWithValue("skips", NpgsqlDbType.Integer, skips);
        run.Parameters.AddWithValue("pending", NpgsqlDbType.Integer, r.Pending);
        run.Parameters.AddWithValue("integrity", NpgsqlDbType.Integer, r.Integrity);
        run.Parameters.AddWithValue("manual", NpgsqlDbType.Boolean, r.Manual);
        run.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, r.Reason);
        await run.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SessionWritten(entries, exits, skips);
    }

    public async ValueTask<IReadOnlyList<PaperEntryView>> ListEntriesAsync(DateOnly? from, DateOnly? to, Guid? accountId, long? epochId, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT e.id, e.plan_id, e.plan_item_id, e.account_id, e.epoch_id, e.symbol, e.side, e.strategy_id, e.cell_key, e.ticket_group, e.approval_kind, e.qty_allocated, e.qty, e.armed_at, e.entry_at,
                   e.entry_price, e.stop_price, e.target_price, e.basis,
                   x.id, x.exit_at, x.exit_price, x.exit_reason, x.held_sessions, x.gross_pnl, x.charges, x.net_pnl, x.r_exact, x.r_sim, x.r_basis, x.mae_r, x.mfe_r, x.cost_basis
            FROM paper.entries e LEFT JOIN paper.exits x ON x.entry_id = e.id
            WHERE (@from IS NULL OR (e.entry_at AT TIME ZONE 'Asia/Kolkata')::date >= @from)
              AND (@to IS NULL OR (e.entry_at AT TIME ZONE 'Asia/Kolkata')::date <= @to)
              AND (@account IS NULL OR e.account_id = @account)
              AND (@epoch IS NULL OR e.epoch_id = @epoch)
            ORDER BY e.entry_at DESC, e.id DESC
            LIMIT @limit
            """);
        select.Parameters.AddWithValue("from", NpgsqlDbType.Date, (object?)from ?? DBNull.Value);
        select.Parameters.AddWithValue("to", NpgsqlDbType.Date, (object?)to ?? DBNull.Value);
        select.Parameters.AddWithValue("account", NpgsqlDbType.Uuid, (object?)accountId ?? DBNull.Value);
        select.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, (object?)epochId ?? DBNull.Value);
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        var items = new List<PaperEntryView>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            decimal? M(int i) => reader.IsDBNull(i) ? null : reader.GetDecimal(i);
            var exit = reader.IsDBNull(19)
                ? null
                : new PaperExitView(reader.GetFieldValue<DateTimeOffset>(20), reader.GetDecimal(21), reader.GetString(22), reader.GetInt32(23), reader.GetDecimal(24), M(25), M(26), reader.GetDouble(27), reader.GetDouble(28), reader.GetString(29), reader.GetDouble(30), reader.GetDouble(31));
            items.Add(new PaperEntryView(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetGuid(3), reader.GetInt64(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10),
                reader.GetInt32(11), reader.GetInt32(12), reader.GetFieldValue<DateTimeOffset>(13), reader.GetFieldValue<DateTimeOffset>(14), reader.GetDecimal(15), reader.GetDecimal(16), M(17), exit, reader.GetString(18),
                reader.IsDBNull(32) ? PaperWording.Gross : reader.GetString(32)));
        }

        return items;
    }

    public async ValueTask<IReadOnlyList<CaptureRunView>> ListRunsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT id, session_date, started_at, finished_at, outcome, seen, entries_written, exits_written, skips_written, pending, integrity, manual, reason
            FROM paper.capture_runs ORDER BY session_date DESC, id DESC LIMIT @limit
            """);
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        var items = new List<CaptureRunView>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CaptureRunView(
                reader.GetInt64(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6),
                reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetBoolean(11), reader.GetString(12)));
        }

        return items;
    }

    private static async Task<int> InsertExitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long entryId, DateTimeOffset entryAt, PaperExit x, CancellationToken cancellationToken)
    {
        await using var insert = new NpgsqlCommand("""
            INSERT INTO paper.exits (entry_id, entry_at, exit_at, exit_price, exit_reason, held_sessions, gross_pnl, charges, charge_sheet_id, net_pnl, cost_basis, r_exact, r_gross, r_net, r_sim, r_basis, mae_r, mfe_r)
            VALUES (@entry, @entryat, @exitat, @price, @reason, @held, @gross, @charges, @sheet, @net, @cost, @rexact, @rgross, @rnet, @rsim, @rbasis, @mae, @mfe)
            ON CONFLICT (entry_id) DO NOTHING
            """, connection, transaction);
        var p = insert.Parameters;
        p.AddWithValue("entry", NpgsqlDbType.Bigint, entryId);
        p.AddWithValue("entryat", NpgsqlDbType.TimestampTz, entryAt);
        p.AddWithValue("exitat", NpgsqlDbType.TimestampTz, x.ExitAt);
        p.AddWithValue("price", NpgsqlDbType.Numeric, x.ExitPrice);
        p.AddWithValue("reason", NpgsqlDbType.Varchar, x.ExitReason);
        p.AddWithValue("held", NpgsqlDbType.Integer, x.HeldSessions);
        p.AddWithValue("gross", NpgsqlDbType.Numeric, x.GrossPnl);
        p.AddWithValue("charges", NpgsqlDbType.Numeric, (object?)x.Charges ?? DBNull.Value);
        p.AddWithValue("sheet", NpgsqlDbType.Varchar, (object?)x.ChargeSheetId ?? DBNull.Value);
        p.AddWithValue("net", NpgsqlDbType.Numeric, (object?)x.NetPnl ?? DBNull.Value);
        p.AddWithValue("cost", NpgsqlDbType.Varchar, x.CostBasis);
        p.AddWithValue("rexact", NpgsqlDbType.Double, x.RExact);
        p.AddWithValue("rgross", NpgsqlDbType.Double, x.RGross);
        p.AddWithValue("rnet", NpgsqlDbType.Double, (object?)x.RNet ?? DBNull.Value);
        p.AddWithValue("rsim", NpgsqlDbType.Double, x.RSim);
        p.AddWithValue("rbasis", NpgsqlDbType.Varchar, x.RBasis);
        p.AddWithValue("mae", NpgsqlDbType.Double, x.MaeR);
        p.AddWithValue("mfe", NpgsqlDbType.Double, x.MfeR);
        return await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
