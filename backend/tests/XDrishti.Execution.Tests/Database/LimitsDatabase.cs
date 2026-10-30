using System.Globalization;
using Npgsql;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Limits;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>Its own server: these tests write global settings rows and accounts whose effective mode is <c>semi_auto</c> or <c>auto_live</c>, which the shared execution fixture cannot promise.</summary>
[CollectionDefinition(Name)]
public sealed class LimitsDatabaseSuite : ICollectionFixture<ExecDatabase>
{
    public const string Name = "limits-database";
}

/// <summary>
/// The rows the record cannot reach yet, set up the way <see cref="ExecutionTicketsDatabaseTests"/> does: an account whose effective mode is <c>auto_live</c> or <c>semi_auto</c> (one trail row written by the owner past the constraint and the
/// trigger, both put back at once; P12-07 still refuses every new such row), and a limits sheet in force now (written by the owner past the raise rule and, for an all-day window, past the window constraint, put back NOT VALID).
/// </summary>
internal sealed class LimitsDb(ExecDatabase db)
{
    public const string Owner = "owner:hem@example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task<Guid> AccountAsync(string? mode = "auto_live")
    {
        var account = Guid.NewGuid();
        var tag = account.ToString("N")[..10];
        var client = Random.Shared.NextInt64(2_000_000_000, 9_000_000_000).ToString(CultureInfo.InvariantCulture);
        await ExecDatabase.ExecuteAsync(
            db.Owner,
            $"""
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES ('{account}', 'lim-{tag}', 'Limits {tag}', 'Dhan', '{client}', current_date, '#112233', now(), now(), false, false, false, 0);
            INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments)
            VALUES ('{account}', true, 'ManualLive', 'Fixed', 600000, true, false, 1, 0, 0);
            """);
        if (mode is not null)
        {
            await using var connection = new NpgsqlConnection(db.Owner);
            await connection.OpenAsync(Ct);
            await using var tx = await connection.BeginTransactionAsync(Ct);
            await using var command = new NpgsqlCommand(
                $"""
                ALTER TABLE execution.mode_trail DROP CONSTRAINT IF EXISTS ck_exec_mode_trail_modes;
                ALTER TABLE execution.mode_trail DISABLE TRIGGER trg_exec_mode_trail_check;
                INSERT INTO execution.mode_trail (account_id, from_mode, to_mode, kind, actor, reason, gate_snapshot, gate_hash, effective_from)
                VALUES ('{account}', 'manual_live', '{mode}', 'owner_demotion', '{Owner}', 'test only: no slice can store {mode} (ADR 0163)', jsonb_build_object(), repeat('b', 64), now() - interval '1 minute');
                ALTER TABLE execution.mode_trail ENABLE TRIGGER trg_exec_mode_trail_check;
                ALTER TABLE execution.mode_trail ADD CONSTRAINT ck_exec_mode_trail_modes CHECK (from_mode IN ('paper','manual_live','assisted') AND to_mode IN ('paper','manual_live','assisted') AND from_mode <> to_mode) NOT VALID;
                """, connection, tx);
            await command.ExecuteNonQueryAsync(Ct);
            await tx.CommitAsync(Ct);
        }

        return account;
    }

    /// <summary>A settings row (the newest one is in force for everyone on this server) with P12-01's entry window opened to the whole day, so that the tests run at any hour; answers its id.</summary>
    public async Task<long> SwitchesAsync(bool execution, bool autoLive) =>
        (long)(await ExecDatabase.ScalarAsync(
            db.Owner,
            $$"""INSERT INTO execution.settings (execution_enabled, auto_live_enabled, "values", actor, reason) VALUES ({{execution.ToString().ToLowerInvariant()}}, {{autoLive.ToString().ToLowerInvariant()}}, '{"exec.entry_window_start": "00:00", "exec.entry_window_end": "23:59"}', '{{Owner}}', 'test') RETURNING id"""))!;

    /// <summary>
    /// The hand example's sheet, in force now. <paramref name="window"/> null is all day (00:00 to 23:59:59, past the window constraint so that the tests run at any hour); otherwise the given India window, which the constraint admits.
    /// </summary>
    public async Task<long> SheetAsync(Guid account, int orders = 6, decimal perOrder = 60_000m, decimal perDay = 150_000m, string allow = "'[\"TATAPOWER\",\"INFY\",\"SBIN\"]'", (string Start, string End)? window = null, bool complete = true)
    {
        var (start, end) = window ?? ("00:00", "23:59:59");
        var values = complete
            ? $"{orders}, {perOrder.ToString(CultureInfo.InvariantCulture)}, {perDay.ToString(CultureInfo.InvariantCulture)}, 1500, 3, {allow}::jsonb, '{start}', '{end}', ARRAY['EQ_MIS']"
            : $"{orders}, {perOrder.ToString(CultureInfo.InvariantCulture)}, {perDay.ToString(CultureInfo.InvariantCulture)}, NULL, 3, {allow}::jsonb, '{start}', '{end}', ARRAY['EQ_MIS']";
        await using var connection = new NpgsqlConnection(db.Owner);
        await connection.OpenAsync(Ct);
        await using var tx = await connection.BeginTransactionAsync(Ct);
        await using var command = new NpgsqlCommand(
            $"""
            ALTER TABLE execution.limits DROP CONSTRAINT IF EXISTS ck_exec_limits_window;
            ALTER TABLE execution.limits DISABLE TRIGGER trg_exec_limits_check;
            INSERT INTO execution.limits (account_id, max_orders_per_day, max_notional_per_order, max_notional_per_day, max_loss_per_day, max_open_positions, allow_list, window_start, window_end, products, actor, reason, effective_from, snapshot_hash)
            VALUES ('{account}', {values}, '{Owner}', 'test sheet', now() - interval '1 minute', repeat('c', 64));
            ALTER TABLE execution.limits ENABLE TRIGGER trg_exec_limits_check;
            ALTER TABLE execution.limits ADD CONSTRAINT ck_exec_limits_window CHECK ((window_start IS NULL) = (window_end IS NULL) AND window_start >= time '09:16' AND window_end <= time '15:00' AND window_start < window_end) NOT VALID;
            SELECT max(id) FROM execution.limits WHERE account_id = '{account}';
            """, connection, tx);
        var id = (long)(await command.ExecuteScalarAsync(Ct))!;
        await tx.CommitAsync(Ct);
        return id;
    }

    public async Task<DateOnly> TodayAsync() =>
        DateOnly.Parse((string)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT ((clock_timestamp() AT TIME ZONE 'Asia/Kolkata')::date)::text"))!, CultureInfo.InvariantCulture);

    /// <summary>A reservation through the real ledger store as xd_exec: the action-log row and the ledger row in one transaction.</summary>
    public async Task<LedgerResult> ReserveAsync(Guid account, long limitsId, long settingsId, decimal notional, string symbol = "NSE:TATAPOWER", string product = "EQ_MIS", string side = "buy", string mode = "auto_live", NpgsqlDataSource? shared = null)
    {
        await using var own = shared is null ? NpgsqlDataSource.Create(db.Exec) : null;
        var data = shared ?? own!;
        var intent = Guid.NewGuid();
        var kind = mode == "auto_live" ? ActionKinds.AutoEntry : ActionKinds.TapEntry;
        var by = mode == "auto_live" ? ActionKinds.ByAuto : ActionKinds.ByTap;
        return await new PgLedger(data).ReserveAsync(
            new LedgerReservation(account, await TodayAsync(), intent, symbol, "strategy-v1", product, side, 1, notional, 0m, mode, limitsId, settingsId),
            new ActionLogEntry(account, kind, by, intent, null, "Placed BUY 1 TATAPOWER (test)", [], settingsId, limitsId),
            Ct);
    }

    /// <summary>A pool of at most five connections as xd_exec (the role's limit is six): parallel reservations queue on it and still race for the advisory lock.</summary>
    public NpgsqlDataSource Pool() => NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(db.Exec) { Pooling = true, MaxPoolSize = 5 }.ConnectionString);

    public async Task<long> CountAsync(string table, Guid account) => (long)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT count(*) FROM execution.{table} WHERE account_id = '{account}'"))!;

    public static async Task<string> RefusalAsync(string connection, string sql)
    {
        try
        {
            await ExecDatabase.ExecuteAsync(connection, sql);
            return string.Empty;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState + " " + failure.MessageText;
        }
    }
}
