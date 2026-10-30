using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using XDrishti.Execution.Application.Limits;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Limits;
using XDrishti.Execution.Persistence;
using XDrishti.Execution.Tests.Limits;

namespace XDrishti.Execution.Tests.Database;

/// <summary>
/// Spec P12-09 criteria 2, 10, 11 and 13 on a real database with the real roles: the trigger <c>execution.check_ledger_entry</c> holds the hand example's caps exactly, refuses every row that breaks the sheet in force, the mode, the
/// switches or the same-transaction action-log rule, serialises concurrent entries of one account so that only one takes the last slot, and needs no rate limiter for the daily caps; the three tables are insert-only.
/// </summary>
[Collection(LimitsDatabaseSuite.Name)]
public sealed class LedgerDatabaseTests(ExecDatabase db)
{
    private readonly LimitsDb _db = new(db);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_hand_example_holds_in_the_database_three_of_49482_pass_a_fourth_is_refused_and_1554_reaches_exactly_150000()
    {
        var account = await _db.AccountAsync();
        var settings = await _db.SwitchesAsync(true, true);
        var sheet = await _db.SheetAsync(account);

        for (var i = 0; i < 3; i++)
        {
            (await _db.ReserveAsync(account, sheet, settings, 49_482.00m)).Reserved.ShouldBeTrue();
        }

        (await _db.ReserveAsync(account, sheet, settings, 49_482.00m)).Refused.ShouldBe(AutoLiveCodes.NotionalDay);
        (await _db.ReserveAsync(account, sheet, settings, 1_554.01m)).Refused.ShouldBe(AutoLiveCodes.NotionalDay);
        (await _db.ReserveAsync(account, sheet, settings, 1_554.00m)).Reserved.ShouldBeTrue();
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT sum(notional) FROM execution.ledger_entries WHERE account_id = '{account}'")).ShouldBe(150_000.00m);
        (await _db.CountAsync("ledger_entries", account)).ShouldBe(4L);
        (await _db.CountAsync("action_log", account)).ShouldBe(4L, "a refused reservation leaves no row of its transaction");
    }

    [Fact]
    public async Task The_seventh_order_and_an_order_above_60000_are_refused_and_the_daily_caps_need_no_rate_limiter()
    {
        var account = await _db.AccountAsync();
        var settings = await _db.SwitchesAsync(true, true);
        var sheet = await _db.SheetAsync(account);

        (await _db.ReserveAsync(account, sheet, settings, 60_000.01m)).Refused.ShouldBe(AutoLiveCodes.NotionalOrder);
        (await _db.ReserveAsync(account, sheet, settings, 60_000.00m)).Reserved.ShouldBeTrue();
        var results = new List<LedgerResult>();
        for (var i = 0; i < 6; i++)
        {
            results.Add(await _db.ReserveAsync(account, sheet, settings, 1.00m)); // back to back, no limiter between them
        }

        results.Take(5).ShouldAllBe(r => r.Reserved);
        results[5].Refused.ShouldBe(AutoLiveCodes.OrdersDay);
        (await _db.CountAsync("ledger_entries", account)).ShouldBe(6L);
    }

    [Fact]
    public async Task Twenty_parallel_reservations_for_the_last_slot_leave_exactly_one()
    {
        var account = await _db.AccountAsync();
        var settings = await _db.SwitchesAsync(true, true);
        var sheet = await _db.SheetAsync(account, orders: 3);
        (await _db.ReserveAsync(account, sheet, settings, 10m)).Reserved.ShouldBeTrue();
        (await _db.ReserveAsync(account, sheet, settings, 10m)).Reserved.ShouldBeTrue();

        await using var pool = _db.Pool();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _db.ReserveAsync(account, sheet, settings, 10m, shared: pool)));

        results.Count(r => r.Reserved).ShouldBe(1);
        results.Where(r => !r.Reserved).ShouldAllBe(r => r.Refused == AutoLiveCodes.OrdersDay);
        (await _db.CountAsync("ledger_entries", account)).ShouldBe(3L);
    }

    [Fact]
    public async Task Twenty_parallel_reservations_cannot_both_take_the_last_rupees_of_the_day()
    {
        var account = await _db.AccountAsync();
        var settings = await _db.SwitchesAsync(true, true);
        var sheet = await _db.SheetAsync(account, orders: 20, perOrder: 1_000m, perDay: 1_000m);

        await using var pool = _db.Pool();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _db.ReserveAsync(account, sheet, settings, 600m, shared: pool)));

        results.Count(r => r.Reserved).ShouldBe(1);
        results.Where(r => !r.Reserved).ShouldAllBe(r => r.Refused == AutoLiveCodes.NotionalDay);
    }

    [Fact]
    public async Task The_database_refuses_a_symbol_off_the_list_a_product_or_side_not_allowed_and_an_entry_outside_the_window()
    {
        var account = await _db.AccountAsync();
        var settings = await _db.SwitchesAsync(true, true);
        var sheet = await _db.SheetAsync(account);

        (await _db.ReserveAsync(account, sheet, settings, 10m, symbol: "NSE:TATAPOWER-EQ")).Refused.ShouldBe(AutoLiveCodes.InstrumentNotAllowed);
        (await _db.ReserveAsync(account, sheet, settings, 10m, symbol: "NSE:ITC")).Refused.ShouldBe(AutoLiveCodes.InstrumentNotAllowed);
        (await _db.ReserveAsync(account, sheet, settings, 10m, product: "EQ_CNC")).Refused.ShouldBe(AutoLiveCodes.ProductNotAllowed);
        (await _db.ReserveAsync(account, sheet, settings, 10m, side: "sell")).Refused.ShouldBe(AutoLiveCodes.ProductNotAllowed);
        (await _db.ReserveAsync(account, sheet, settings, 10m, symbol: "NSE:INFY")).Reserved.ShouldBeTrue();

        var closed = await _db.AccountAsync();
        var india = TimeOnly.Parse((string)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT to_char(clock_timestamp() AT TIME ZONE 'Asia/Kolkata', 'HH24:MI:SS')"))!, CultureInfo.InvariantCulture);
        var window = india >= new TimeOnly(9, 16) && india < new TimeOnly(9, 18) ? ("14:58", "15:00") : ("09:16", "09:17");
        var closedSheet = await _db.SheetAsync(closed, window: window);
        (await _db.ReserveAsync(closed, closedSheet, settings, 10m)).Refused.ShouldBe(AutoLiveCodes.WindowClosed);
    }

    [Fact]
    public async Task The_database_refuses_a_mode_that_is_not_semi_auto_or_auto_live_the_switches_off_and_a_sheet_that_is_incomplete_or_not_in_force()
    {
        var manual = await _db.AccountAsync(mode: null);
        var auto = await _db.AccountAsync();
        var semi = await _db.AccountAsync(mode: "semi_auto");
        var incomplete = await _db.AccountAsync();
        var older = await _db.SwitchesAsync(true, true);
        var settings = await _db.SwitchesAsync(true, true);
        var manualSheet = await _db.SheetAsync(manual);
        var autoSheet = await _db.SheetAsync(auto);
        var semiSheet = await _db.SheetAsync(semi);
        var incompleteSheet = await _db.SheetAsync(incomplete, complete: false);

        (await _db.ReserveAsync(manual, manualSheet, settings, 10m)).Refused.ShouldBe(AutoLiveCodes.NotAuto);
        (await _db.ReserveAsync(auto, autoSheet, settings, 10m, mode: "semi_auto")).Refused.ShouldBe(AutoLiveCodes.NotAuto);
        (await _db.ReserveAsync(incomplete, incompleteSheet, settings, 10m)).Refused.ShouldBe(AutoLiveCodes.LimitsIncomplete);
        (await _db.ReserveAsync(auto, manualSheet, settings, 10m)).Refused.ShouldBe("limits.stale");
        (await _db.ReserveAsync(auto, autoSheet, older, 10m)).Refused.ShouldBe("settings.stale");

        var autoOff = await _db.SwitchesAsync(true, false);
        (await _db.ReserveAsync(auto, autoSheet, autoOff, 10m)).Refused.ShouldBe(AutoLiveCodes.NotAuto);
        (await _db.ReserveAsync(semi, semiSheet, autoOff, 10m, mode: "semi_auto")).Reserved.ShouldBeTrue("semi_auto needs execution ON only; its caps are the same");

        var executionOff = await _db.SwitchesAsync(false, false);
        (await _db.ReserveAsync(semi, semiSheet, executionOff, 10m, mode: "semi_auto")).Refused.ShouldBe(AutoLiveCodes.NotAuto);
    }

    [Fact]
    public async Task A_ledger_row_needs_an_action_log_row_of_its_own_transaction_and_an_entry_row_needs_its_ledger_row()
    {
        var account = await _db.AccountAsync();
        var settings = await _db.SwitchesAsync(true, true);
        var sheet = await _db.SheetAsync(account);
        var intent = Guid.NewGuid();
        var session = await _db.TodayAsync();
        string Ledger(string action) =>
            $"INSERT INTO execution.ledger_entries (account_id, session_date, intent_public_id, symbol, strategy_hash, product, side, quantity, notional, risk_at_stop, mode, limits_id, settings_id, action_log_id) VALUES ('{account}', '{session:yyyy-MM-dd}', '{intent}', 'NSE:INFY', 'h', 'EQ_MIS', 'buy', 1, 10, 0, 'auto_live', {sheet}, {settings}, {action})";
        var entryRow = $"INSERT INTO execution.action_log (account_id, session_date, kind, \"by\", intent_public_id, text, settings_id, limits_id) VALUES ('{account}', current_date, 'auto_entry', 'auto', '{intent}', 'Placed', {settings}, {sheet})";

        (await LimitsDb.RefusalAsync(db.Exec, entryRow)).ShouldContain("ledger.missing");
        var notice = (long)(await ExecDatabase.ScalarAsync(db.Exec, $"INSERT INTO execution.action_log (account_id, session_date, kind, \"by\", text, codes) VALUES ('{account}', current_date, 'auto_refused', 'auto', 'Did not place', '[\"feed.stale\"]') RETURNING id"))!;
        (await LimitsDb.RefusalAsync(db.Exec, Ledger(notice.ToString(CultureInfo.InvariantCulture)))).ShouldContain("action_log.missing");

        await using (var connection = new NpgsqlConnection(db.Exec))
        {
            await connection.OpenAsync(Ct);
            await using var tx = await connection.BeginTransactionAsync(Ct);
            await using var first = new NpgsqlCommand(entryRow + " RETURNING id", connection, tx);
            var id = (long)(await first.ExecuteScalarAsync(Ct))!;
            await using var second = new NpgsqlCommand(Ledger(id.ToString(CultureInfo.InvariantCulture)), connection, tx);
            await second.ExecuteNonQueryAsync(Ct);
            await tx.CommitAsync(Ct);
        }

        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT concat_ws('|', l.mode, a.kind, a.\"by\", a.session_date = (a.at AT TIME ZONE 'Asia/Kolkata')::date) FROM execution.ledger_entries l JOIN execution.action_log a ON a.id = l.action_log_id WHERE l.intent_public_id = '{intent}'"))
            .ShouldBe("auto_live|auto_entry|auto|t");
    }

    [Fact]
    public async Task The_three_tables_are_insert_only_and_the_runtime_roles_write_only_what_the_spec_names()
    {
        var account = await _db.AccountAsync();
        var settings = await _db.SwitchesAsync(true, true);
        var sheet = await _db.SheetAsync(account);
        (await _db.ReserveAsync(account, sheet, settings, 10m)).Reserved.ShouldBeTrue();

        foreach (var table in new[] { "limits", "ledger_entries", "action_log" })
        {
            (await ExecDatabase.SqlStateAsync(db.Owner, $"UPDATE execution.{table} SET account_id = account_id WHERE account_id = '{account}'")).ShouldBe("23514", table);
            (await ExecDatabase.SqlStateAsync(db.Owner, $"DELETE FROM execution.{table} WHERE account_id = '{account}'")).ShouldBe("23514", table);
            (await ExecDatabase.SqlStateAsync(db.Owner, $"TRUNCATE execution.{table} CASCADE")).ShouldBe("23514", table);
            (await ExecDatabase.SqlStateAsync(db.App, $"SELECT count(*) FROM execution.{table}")).ShouldBeNull(table);
            (await ExecDatabase.SqlStateAsync(db.Exec, $"SELECT count(*) FROM execution.{table}")).ShouldBeNull(table);
        }

        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.action_log (session_date, kind, \"by\", text, codes) VALUES (current_date, 'auto_refused', 'auto', 'x', '[\"a\"]')")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.ledger_entries (account_id, session_date, intent_public_id, symbol, strategy_hash, product, side, quantity, notional, risk_at_stop, mode, limits_id, settings_id, action_log_id) VALUES ('{account}', current_date, gen_random_uuid(), 'NSE:INFY', 'h', 'EQ_MIS', 'buy', 1, 1, 0, 'auto_live', {sheet}, {settings}, 1)")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.limits (account_id, actor, reason, effective_from, snapshot_hash) VALUES ('{account}', '{LimitsDb.Owner}', 'x', now() + interval '3 days', repeat('a', 64))")).ShouldBe("42501");
    }

    private sealed class AutoLiveMode(ExecMode mode) : IModeSource
    {
        public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<ModeAnswer?>(new ModeAnswer(mode, true));
    }

    private sealed class Seen : IOwnerPresence
    {
        public Task<OwnerSeen?> NewestAsync(CancellationToken cancellationToken) => Task.FromResult<OwnerSeen?>(new OwnerSeen(Noon(), false));

        public static DateTimeOffset Noon() => new(XDrishti.Domain.Market.IndiaTime.Today(DateTimeOffset.UtcNow).ToDateTime(new TimeOnly(11, 59, 58)), XDrishti.Domain.Market.IndiaTime.Offset);
    }

    private sealed class Fresh : IFeedHealth
    {
        public Task<DateTimeOffset?> LastPriceAtAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult<DateTimeOffset?>(Seen.Noon());
    }

    /// <summary>
    /// The sheet in the database has an all-day window so that the trigger admits the row at any hour; the guard would refuse that window (outside 09:16 to 15:00), so the guard is shown the hand example's window and a clock of 12:00 India
    /// time of today. The time-of-day rules are the unit tests' (AutoLiveGuardTests); this test is about the rows, the roles and the database's own judgement.
    /// </summary>
    private sealed class Midday(ILimitsStore inner) : ILimitsStore
    {
        public async Task<LimitsReading> ReadAsync(Guid accountId, CancellationToken cancellationToken)
        {
            var reading = await inner.ReadAsync(accountId, cancellationToken);
            var noon = new DateTimeOffset(XDrishti.Domain.Market.IndiaTime.Today(reading.DbNow).ToDateTime(new TimeOnly(12, 0)), XDrishti.Domain.Market.IndiaTime.Offset);
            return new LimitsReading(noon, reading.InForce is { } row ? row with { Limits = row.Limits with { WindowStart = new TimeOnly(9, 20), WindowEnd = new TimeOnly(14, 45) } } : null);
        }
    }

    private static ReserveEntry Reserve(NpgsqlDataSource data, ExecMode mode)
    {
        var limits = new PgLimits(data);
        var ledger = new PgLedger(data);
        return new ReserveEntry(new AutoLiveMode(mode), limits, new Midday(limits), ledger, ledger, new Seen(), new Fresh(), NullLogger<ReserveEntry>.Instance);
    }

    [Fact]
    public async Task Reserve_entry_over_the_real_stores_writes_the_auto_entry_with_its_ledger_row_and_a_refusal_with_its_codes()
    {
        var account = await _db.AccountAsync();
        await _db.SwitchesAsync(true, true);
        await _db.SheetAsync(account);
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var entry = LimitsScene.Entry() with { AccountId = account, IntentPublicId = Guid.NewGuid() };

        var first = await Reserve(data, ExecMode.AutoLive).ReserveAsync(entry with { StrategyHash = "never-used" }, LimitsScene.Facts(), Ct);

        first.Status.ShouldBe(ReserveStatus.Refused);
        first.Codes.ShouldBe([AutoLiveCodes.StrategyFirstUse]);
        first.RouteToTicket.ShouldBeTrue();
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT concat_ws('|', kind, \"by\", codes::text) FROM execution.action_log WHERE account_id = '{account}'")).ShouldBe("auto_refused|auto|[\"strategy.first_use\"]");

        var semi = await _db.AccountAsync(mode: "semi_auto");
        await _db.SheetAsync(semi);
        var tapped = await Reserve(data, ExecMode.SemiAuto).ReserveAsync(entry with { AccountId = semi, IntentPublicId = Guid.NewGuid() }, LimitsScene.Facts(), Ct);
        tapped.Status.ShouldBe(ReserveStatus.Reserved);
        var used = await Reserve(data, ExecMode.AutoLive).ReserveAsync(entry with { AccountId = semi, IntentPublicId = Guid.NewGuid() }, LimitsScene.Facts(), Ct);
        used.Codes.ShouldBe([AutoLiveCodes.NotAuto], "the database's effective mode of the account is semi_auto");
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT concat_ws('|', a.kind, a.\"by\", l.mode, l.notional, l.risk_at_stop, a.settings_id = l.settings_id, a.limits_id = l.limits_id) FROM execution.ledger_entries l JOIN execution.action_log a ON a.id = l.action_log_id WHERE l.account_id = '{semi}'"))
            .ShouldBe("tap_entry|tap|semi_auto|49482.00|870.00|t|t");
        ((string)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT text FROM execution.action_log WHERE account_id = '{semi}' AND kind = 'tap_entry'"))!).ShouldStartWith("Placed BUY 120 TATAPOWER at LIMIT 412.35 with stop 405.10 and target 425.80 because you confirmed ticket 4711 at ");
    }
}

/// <summary>Spec P12-09 criterion 12 on the real database: with auto_live_enabled OFF, or execution_enabled OFF, or no settings row, an auto_live account's entry reads the two switches and writes nothing.</summary>
[Collection(LimitsDatabaseSuite.Name)]
public sealed class ExecutionSettingsDatabaseTests(ExecDatabase db)
{
    private readonly LimitsDb _db = new(db);

    private sealed class AutoLiveMode : IModeSource
    {
        public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<ModeAnswer?>(new ModeAnswer(ExecMode.AutoLive, true));
    }

    private sealed class Untouched : IOwnerPresence, IFeedHealth
    {
        public Task<OwnerSeen?> NewestAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("not read when auto_live is off");

        public Task<DateTimeOffset?> LastPriceAtAsync(string symbol, CancellationToken cancellationToken) => throw new InvalidOperationException("not read when auto_live is off");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task With_a_switch_off_the_entry_is_mode_not_auto_and_no_ledger_or_action_log_row_is_written(bool execution, bool autoLive)
    {
        var account = await _db.AccountAsync();
        await _db.SheetAsync(account);
        await _db.SwitchesAsync(execution, autoLive);
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var limits = new PgLimits(data);
        var ledger = new PgLedger(data);
        var reserve = new ReserveEntry(new AutoLiveMode(), limits, limits, ledger, ledger, new Untouched(), new Untouched(), NullLogger<ReserveEntry>.Instance);

        var outcome = await reserve.ReserveAsync(LimitsScene.Entry() with { AccountId = account }, LimitsScene.Facts(), TestContext.Current.CancellationToken);

        outcome.ShouldBe(ReserveOutcome.NotAuto);
        (await _db.CountAsync("action_log", account)).ShouldBe(0L);
        (await _db.CountAsync("ledger_entries", account)).ShouldBe(0L);
    }

    [Fact]
    public async Task The_settings_source_reads_the_newest_row_and_its_auto_live_keys()
    {
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO execution.settings (execution_enabled, auto_live_enabled, \"values\", actor, reason) VALUES (true, true, '{{\"exec.auto_live_attended_min\": \"25\", \"exec.auto_live_require_day_unlock\": \"maybe\"}}', '{LimitsDb.Owner}', 'test')");
        await using var data = NpgsqlDataSource.Create(db.Exec);

        var settings = await ((IAutoLiveSettingsSource)new PgLimits(data)).ReadAsync(TestContext.Current.CancellationToken);

        settings.ShouldNotBeNull();
        (settings.ExecutionEnabled, settings.AutoLiveEnabled, settings.AttendedMinutes, settings.RequireDayUnlock, settings.RequireAttended, settings.AllowAutoApproved).ShouldBe((true, true, 25, true, true, false));
    }
}
