using System.Globalization;
using System.Net;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>
/// Spec P12-10 criteria 1 to 5, 10, 13, 17 and 18 (read models) over HTTP on a real database: five GET routes and two more fields on the status, session required, honest figures on a fixture day (three orders sent, one rejected, one protection
/// breach, one halt, one ticket waiting), 422 execution.invalid on an unknown filter, and no secret in any answer. The record is insert-only, so the test arranges its rows with the user triggers off and removes them again.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExecutionScreensApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string[] Tables =
    [
        "intents", "intent_state", "legs", "fills", "check_runs", "check_results", "alarms", "alarm_clears", "halts", "halt_releases", "halt_auto_clears", "positions", "day_marks", "limits", "settings", "action_log", "ledger_entries", "confirm_tickets",
        "ticket_outcomes",
    ];

    private const string ClientId = "9123456789";

    private readonly Guid _account = Guid.NewGuid();
    private readonly Guid _bare = Guid.NewGuid();
    private readonly long _base = Random.Shared.NextInt64(7_000_000_000, 8_000_000_000);
    private readonly Guid _i1 = Guid.NewGuid();
    private readonly Guid _i2 = Guid.NewGuid();
    private readonly Guid _i3 = Guid.NewGuid();
    private readonly Guid _i4 = Guid.NewGuid();
    private readonly Guid _ticket = Guid.NewGuid();

    private static DateOnly Today => new(2026, 10, 12);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await ResignInAsync();
        await using var connection = await OpenAsync();
        foreach (var (id, client) in new[] { (_account, ClientId), (_bare, "9123456788") })
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget) VALUES (@id, @slug, @name, 'Dhan', @client, current_date, '#112233', now(), now(), false, false, false, 0)",
                ("id", id), ("slug", "s-" + id.ToString("N")), ("name", "Screens " + id.ToString("N")[..8]), ("client", client));
            await ExecuteAsync(connection, "INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments) VALUES (@id, true, 'ManualLive', 'Fixed', 100000, true, false, 1, 0, 0)", ("id", id));
            await ExecuteAsync(connection, "INSERT INTO accounts.portfolio_settings (account_id, enabled, include_in_totals) VALUES (@id, false, true)", ("id", id));
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await using var connection = await OpenAsync();
        await SwitchTriggersAsync(connection, "DISABLE");
        try
        {
            foreach (var sql in new[]
            {
                "DELETE FROM execution.ticket_outcomes WHERE ticket_id IN (SELECT id FROM execution.confirm_tickets WHERE account_id = @a)",
                "DELETE FROM execution.confirm_tickets WHERE account_id = @a",
                "DELETE FROM execution.ledger_entries WHERE account_id = @a",
                "DELETE FROM execution.action_log WHERE account_id = @a",
                "DELETE FROM execution.alarm_clears WHERE alarm_id IN (SELECT id FROM execution.alarms WHERE account_id = @a)",
                "DELETE FROM execution.alarms WHERE account_id = @a",
                "DELETE FROM execution.halts WHERE account_id = @a",
                "DELETE FROM execution.check_results WHERE run_id IN (SELECT id FROM execution.check_runs WHERE intent_id IN (SELECT id FROM execution.intents WHERE account_id = @a))",
                "DELETE FROM execution.check_runs WHERE intent_id IN (SELECT id FROM execution.intents WHERE account_id = @a)",
                "DELETE FROM execution.fills WHERE intent_id IN (SELECT id FROM execution.intents WHERE account_id = @a)",
                "DELETE FROM execution.legs WHERE intent_id IN (SELECT id FROM execution.intents WHERE account_id = @a)",
                "DELETE FROM execution.intent_state WHERE intent_id IN (SELECT id FROM execution.intents WHERE account_id = @a)",
                "DELETE FROM execution.intents WHERE account_id = @a",
                "DELETE FROM execution.positions WHERE account_id = @a",
                "DELETE FROM execution.day_marks WHERE account_id = @a",
                "DELETE FROM execution.limits WHERE account_id = @a",
                "DELETE FROM execution.settings WHERE reason = 'p12-10 test'",
            })
            {
                await ExecuteAsync(connection, sql, ("a", _account));
            }
        }
        finally
        {
            await SwitchTriggersAsync(connection, "ENABLE");
        }

        await base.DisposeAsync();
    }

    private static async Task SwitchTriggersAsync(Npgsql.NpgsqlConnection connection, string verb)
    {
        foreach (var table in Tables)
        {
            await ExecuteAsync(connection, $"ALTER TABLE execution.{table} {verb} TRIGGER USER");
        }
    }

    /// <summary>The fixture day (the pinned Monday): a sheet of 6 orders, Rs 150,000 notional, Rs 1,500 loss and 3 positions; three automatic entries worth Rs 148,446; one rejected order; an INFY position whose stop is missing; one halt; one ticket waiting.</summary>
    private async Task SeedDayAsync()
    {
        await using var connection = await OpenAsync();
        await SwitchTriggersAsync(connection, "DISABLE");
        try
        {
            var b = _base;
            var at = Now.AddMinutes(-30);
            (string, object)[] p = [("a", _account), ("b", b), ("d", Today), ("at", at), ("i1", _i1), ("i2", _i2), ("i3", _i3), ("i4", _i4), ("t", _ticket), ("now", Now)];
            foreach (var sql in new[]
            {
                "INSERT INTO execution.settings (id, execution_enabled, auto_live_enabled, \"values\", actor, reason, changed_at, effective_from, snapshot_hash) VALUES (@b, false, false, '{}', 'owner:t', 'p12-10 test', @at, @at, repeat('a', 64))",
                "INSERT INTO execution.limits (id, account_id, max_orders_per_day, max_notional_per_order, max_notional_per_day, max_loss_per_day, max_open_positions, allow_list, window_start, window_end, products, actor, reason, changed_at, effective_from, snapshot_hash) VALUES (@b, @a, 6, 60000, 150000, 1500, 3, '[\"TATAPOWER\",\"INFY\",\"SBIN\"]', '09:20', '14:45', ARRAY['EQ_MIS'], 'owner:t', 'r', @at, @at, repeat('b', 64))",
                "INSERT INTO execution.intents (id, public_id, account_id, plan_item_id, session_date, symbol, security_id, side, quantity, entry_price, stop_price, target_price, structure, environment, created_by, created_at) VALUES (@b + 1, @i1, @a, @b + 1, @d, 'TATAPOWER', '3426', 'buy', 120, 400, 390, 420, 'super', 'sandbox', 'system:t', @at + interval '1 minute')",
                "INSERT INTO execution.intents (id, public_id, account_id, plan_item_id, session_date, symbol, security_id, side, quantity, entry_price, stop_price, target_price, structure, environment, created_by, created_at) VALUES (@b + 2, @i2, @a, @b + 2, @d, 'HDFCBANK', '1333', 'buy', 1, 1500, 1480, 1540, 'super', 'sandbox', 'system:t', @at + interval '2 minute')",
                "INSERT INTO execution.intents (id, public_id, account_id, plan_item_id, session_date, symbol, security_id, side, quantity, entry_price, stop_price, target_price, structure, environment, created_by, created_at) VALUES (@b + 3, @i3, @a, @b + 3, @d, 'SBIN', '3045', 'buy', 10, 800, 790, 820, 'super', 'sandbox', 'system:t', @at + interval '3 minute')",
                "INSERT INTO execution.intents (id, public_id, account_id, plan_item_id, session_date, symbol, security_id, side, quantity, entry_price, stop_price, target_price, structure, environment, created_by, created_at) VALUES (@b + 4, @i4, @a, @b + 4, @d, 'INFY', '1594', 'buy', 5, 1500, 1480, 1540, 'super', 'sandbox', 'system:t', @at + interval '4 minute')",
                "INSERT INTO execution.intent_state (intent_id, state, seq, filled_qty, updated_at) VALUES (@b + 1, 'protected', 1, 120, @at), (@b + 2, 'rejected', 1, 0, @at), (@b + 3, 'open', 1, 0, @at), (@b + 4, 'filled', 1, 5, @at)",
                "INSERT INTO execution.legs (id, intent_id, kind, client_order_id, broker_order_id, algo_id, status, qty, filled_qty, avg_price, price) VALUES (@b + 1, @b + 1, 'E', 'XD000001AAAAAAE1', 'B1001', 'ALG-9', 'filled', 120, 120, 400.10, 400), (@b + 4, @b + 4, 'E', 'XD000004AAAAAAE1', 'B1004', NULL, 'filled', 5, 5, 1500, 1500)",
                "INSERT INTO execution.fills (leg_id, intent_id, exchange_trade_id, qty, price, at, source) VALUES (@b + 1, @b + 1, 't-' || @b || '-1', 120, 400.10, @at, 'engine'), (@b + 4, @b + 4, 't-' || @b || '-4', 5, 1500, @at, 'engine')",
                "INSERT INTO execution.check_runs (id, kind, intent_id, at, verdict, settings_hash, inputs_hash, valid_until) VALUES (@b + 2, 'full', @b + 2, @at, 'rejected', repeat('c', 16), repeat('d', 16), @at + interval '1 hour')",
                "INSERT INTO execution.check_results (run_id, ord, code, class, outcome, detail, source) VALUES (@b + 2, 1, 'cap_notional', 'hard', 'fail', 'over the cap', 'test')",
                "INSERT INTO execution.action_log (id, account_id, session_date, kind, \"by\", intent_public_id, text, settings_id, limits_id, at) VALUES (@b + 1, @a, @d, 'auto_entry', 'auto', @i1, 'Placed BUY 120 TATAPOWER because the plan item was approved.', @b, @b, @at), (@b + 2, @a, @d, 'auto_entry', 'auto', @i3, 'Placed BUY 10 SBIN because the plan item was approved.', @b, @b, @at + interval '1 minute'), (@b + 3, @a, @d, 'auto_entry', 'auto', @i4, 'Placed BUY 5 INFY because the plan item was approved.', @b, @b, @at + interval '2 minute')",
                "INSERT INTO execution.ledger_entries (account_id, session_date, intent_public_id, symbol, strategy_hash, product, side, quantity, notional, risk_at_stop, mode, limits_id, settings_id, action_log_id, sent_at) VALUES (@a, @d, @i1, 'TATAPOWER', 's', 'EQ_MIS', 'buy', 120, 48000, 1200, 'auto_live', @b, @b, @b + 1, @at), (@a, @d, @i3, 'SBIN', 's', 'EQ_MIS', 'buy', 10, 50000, 100, 'auto_live', @b, @b, @b + 2, @at), (@a, @d, @i4, 'INFY', 's', 'EQ_MIS', 'buy', 5, 50446, 100, 'auto_live', @b, @b, @b + 3, @at)",
                "INSERT INTO execution.day_marks (account_id, session_date, realised, marked, capital, peak, at) VALUES (@a, @d, -200, -200, 100000, 0, @at)",
                "INSERT INTO execution.positions (account_id, symbol, product, net_qty, avg_price, updated_at) VALUES (@a, 'TATAPOWER', 'EQ_MIS', 120, 400.10, @at), (@a, 'INFY', 'EQ_MIS', 5, 1500, @at)",
                "INSERT INTO execution.alarms (id, at, code, severity, account_id, intent_id, detail) VALUES (@b + 1, @at, 'protection_breach', 'critical', @a, @b + 4, 'no stop at the broker')",
                "INSERT INTO execution.halts (id, at, scope, account_id, level, trigger_code, source, actor, reason) VALUES (@b + 1, @at, 'account', @a, 'entries', 'owner_button', 'owner', 'owner:t', 'the stop button')",
                "INSERT INTO execution.confirm_tickets (id, intent_public_id, account_id, plan_item_id, session_date, attempt, kind, params, text, text_hash, client_order_id, ref_price, notional, risk_at_stop, requires_password, password_reasons, issued_at, expires_at) VALUES (@t, @i3, @a, @b + 3, @d, 1, 'entry', '{}', 'BUY 10 SBIN limit 800.00', encode(sha256(convert_to('BUY 10 SBIN limit 800.00', 'UTF8')), 'hex'), 'XD000003AAAAAAE1', 800, 8000, 100, false, '[]', @now - interval '1 minute', @now + interval '2 minute')",
            })
            {
                await ExecuteAsync(connection, sql, p);
            }
        }
        finally
        {
            await SwitchTriggersAsync(connection, "ENABLE");
        }
    }

    private async Task<JsonElement> GetAsync(string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(expected);
        var text = await response.Content.ReadAsStringAsync(Ct);
        text.ShouldNotContain(ClientId);
        text.ShouldNotContain("clientId", Case.Insensitive);
        text.ShouldNotContain("token", Case.Insensitive);
        text.ShouldNotContain("balance", Case.Insensitive);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<JsonElement> AccountOverviewAsync(Guid id)
    {
        var overview = await GetAsync("/api/execution/overview?date=2026-10-12");
        return overview.GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("accountId").GetGuid() == id);
    }

    private static decimal Number(JsonElement metric, string name) => metric.GetProperty(name).GetDecimal();

    [Theory]
    [InlineData("/api/execution/overview")]
    [InlineData("/api/execution/blotter")]
    [InlineData("/api/execution/positions")]
    [InlineData("/api/execution/activity")]
    [InlineData("/api/execution/notices")]
    public async Task Without_a_session_every_screen_route_is_401(string path)
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_status_carries_the_four_new_fields_and_nothing_is_automated_today()
    {
        var status = await GetAsync("/api/execution/status");

        status.GetProperty("anyAutomated").GetBoolean().ShouldBeFalse();
        status.GetProperty("pollSeconds").GetInt32().ShouldBe(60);
        status.GetProperty("marketOpen").GetBoolean().ShouldBeTrue();
        status.GetProperty("killSwitchActive").ValueKind.ShouldBeOneOf(JsonValueKind.True, JsonValueKind.False);
        status.GetProperty("entriesBlocked").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task An_account_with_no_sheet_and_no_orders_shows_zero_used_and_no_cap_and_the_canary_is_pending()
    {
        var overview = await GetAsync("/api/execution/overview?date=2026-10-12");
        var account = overview.GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("accountId").GetGuid() == _bare);

        account.GetProperty("caps").GetProperty("orders").GetProperty("state").GetString().ShouldBe("value");
        account.GetProperty("caps").GetProperty("orders").GetProperty("cap").ValueKind.ShouldBe(JsonValueKind.Null);
        account.GetProperty("caps").GetProperty("orders").GetProperty("reason").GetString().ShouldBe("no_limits_sheet");
        account.GetProperty("readiness").GetProperty("state").GetString().ShouldBe("unavailable");
        account.GetProperty("mode").GetString().ShouldBe("manual_live");
        overview.GetProperty("canaryPending").GetBoolean().ShouldBeTrue();
        overview.GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_overview_of_the_fixture_day_shows_used_against_the_sheet_in_force()
    {
        await SeedDayAsync();

        var account = await AccountOverviewAsync(_account);
        var caps = account.GetProperty("caps");

        Number(caps.GetProperty("orders"), "value").ShouldBe(3m);
        Number(caps.GetProperty("orders"), "cap").ShouldBe(6m);
        Number(caps.GetProperty("notional"), "value").ShouldBe(148446m);
        Number(caps.GetProperty("notional"), "cap").ShouldBe(150000m);
        Number(caps.GetProperty("worstCaseLoss"), "value").ShouldBe(1512m);
        Number(caps.GetProperty("worstCaseLoss"), "cap").ShouldBe(1500m);
        Number(caps.GetProperty("openPositions"), "value").ShouldBe(2m);
        Number(caps.GetProperty("openPositions"), "cap").ShouldBe(3m);
        caps.GetProperty("limitsId").GetInt64().ShouldBe(_base);
        caps.GetProperty("pendingLimits").ValueKind.ShouldBe(JsonValueKind.Null);
        account.GetProperty("openTickets").GetInt32().ShouldBe(1);
        account.GetProperty("halts").GetArrayLength().ShouldBe(1);
        account.GetProperty("alarms").GetArrayLength().ShouldBe(1);
        account.GetProperty("positionsAtRisk").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task A_past_date_cannot_show_current_positions_and_says_so()
    {
        var overview = await GetAsync("/api/execution/overview?date=2026-10-09");
        var account = overview.GetProperty("accounts").EnumerateArray().First();

        account.GetProperty("caps").GetProperty("openPositions").GetProperty("state").GetString().ShouldBe("unavailable");
        account.GetProperty("caps").GetProperty("openPositions").GetProperty("reason").GetString().ShouldBe("positions_are_current_only");
    }

    [Fact]
    public async Task The_blotter_lists_the_day_newest_first_with_slippage_codes_and_who_caused_each_order()
    {
        await SeedDayAsync();

        var rows = (await GetAsync($"/api/execution/blotter?date=2026-10-12&accountId={_account}")).EnumerateArray().ToList();

        rows.Select(r => r.GetProperty("symbol").GetString()).ShouldBe(["INFY", "SBIN", "HDFCBANK", "TATAPOWER"]);
        var tata = rows[3];
        tata.GetProperty("state").GetString().ShouldBe("protected");
        tata.GetProperty("stateGroup").GetString().ShouldBe("live");
        tata.GetProperty("stateKnown").GetBoolean().ShouldBeTrue();
        tata.GetProperty("by").GetString().ShouldBe("auto");
        tata.GetProperty("algoId").GetString().ShouldBe("ALG-9");
        tata.GetProperty("brokerOrderId").GetString().ShouldBe("B1001");
        Number(tata, "fillPrice").ShouldBe(400.10m);
        tata.GetProperty("slippageTicks").GetProperty("state").GetString().ShouldBe("unavailable");
        Number(tata.GetProperty("slippagePercent"), "value").ShouldBe(0.025m);
        tata.GetProperty("fills").GetArrayLength().ShouldBe(1);
        var rejected = rows[2];
        rejected.GetProperty("state").GetString().ShouldBe("rejected");
        rejected.GetProperty("canary").GetBoolean().ShouldBeTrue();
        rejected.GetProperty("rejectionCodes")[0].GetString().ShouldBe("cap_notional");
        rejected.GetProperty("slippagePercent").GetProperty("state").GetString().ShouldBe("empty");
        rejected.GetProperty("algoId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_blotter_filters_by_state_group_and_pages_and_refuses_an_unknown_filter_with_422()
    {
        await SeedDayAsync();

        var stopped = await GetAsync($"/api/execution/blotter?date=2026-10-12&accountId={_account}&stateGroup=stopped");
        var second = await GetAsync($"/api/execution/blotter?date=2026-10-12&accountId={_account}&page=2");
        var refused = await GetAsync("/api/execution/blotter?stateGroup=sideways", HttpStatusCode.UnprocessableEntity);
        var page = await GetAsync("/api/execution/blotter?page=0", HttpStatusCode.UnprocessableEntity);

        stopped.GetArrayLength().ShouldBe(1);
        second.GetArrayLength().ShouldBe(0);
        refused.GetProperty("code").GetString().ShouldBe("execution.invalid");
        page.GetProperty("code").GetString().ShouldBe("execution.invalid");
    }

    [Fact]
    public async Task Positions_show_missing_protection_from_the_alarm_and_never_confirm_without_the_broker_order_book()
    {
        await SeedDayAsync();

        var rows = (await GetAsync($"/api/execution/positions?accountId={_account}")).EnumerateArray().ToDictionary(r => r.GetProperty("symbol").GetString()!);

        rows["INFY"].GetProperty("protection").GetString().ShouldBe("missing");
        rows["TATAPOWER"].GetProperty("protection").GetString().ShouldBe("unknown");
        rows["TATAPOWER"].GetProperty("source").GetString().ShouldBe("ours");
        rows["TATAPOWER"].GetProperty("reconciliation").GetString().ShouldBe("not_run");
        Number(rows["TATAPOWER"].GetProperty("riskToStop"), "value").ShouldBe(1212m);
        rows["TATAPOWER"].GetProperty("unrealised").GetProperty("state").GetString().ShouldBe("unavailable");
    }

    [Fact]
    public async Task The_activity_lists_the_log_with_the_halt_newest_first_and_filters_by_kind_and_actor()
    {
        await SeedDayAsync();

        var all = (await GetAsync("/api/execution/activity?date=2026-10-12")).EnumerateArray().Where(r => r.GetProperty("accountId").ValueKind != JsonValueKind.Null && r.GetProperty("accountId").GetGuid() == _account).ToList();
        var autos = (await GetAsync("/api/execution/activity?kind=auto_entry&by=auto")).EnumerateArray().Where(r => r.GetProperty("accountId").ValueKind != JsonValueKind.Null && r.GetProperty("accountId").GetGuid() == _account).ToList();
        var refused = await GetAsync("/api/execution/activity?kind=nonsense", HttpStatusCode.UnprocessableEntity);
        var refusedBy = await GetAsync("/api/execution/activity?by=nobody", HttpStatusCode.UnprocessableEntity);

        all.Select(r => r.GetProperty("kind").GetString()).ShouldContain("halt");
        all.Select(r => r.GetProperty("kind").GetString()).ShouldContain("alarm");
        autos.Count.ShouldBe(3);
        autos.Select(r => r.GetProperty("text").GetString()).ShouldContain("Placed BUY 120 TATAPOWER because the plan item was approved.");
        refused.GetProperty("code").GetString().ShouldBe("execution.invalid");
        refusedBy.GetProperty("code").GetString().ShouldBe("execution.invalid");
    }

    [Fact]
    public async Task The_notices_name_the_waiting_ticket_the_halt_the_breach_and_the_rejection_and_list_automatic_actions_as_resolved()
    {
        await SeedDayAsync();

        var items = (await GetAsync("/api/execution/notices")).GetProperty("items").EnumerateArray().Where(i => i.GetProperty("accountId").ValueKind != JsonValueKind.Null && i.GetProperty("accountId").GetGuid() == _account).ToList();
        var byKind = items.GroupBy(i => i.GetProperty("kind").GetString()!).ToDictionary(g => g.Key, g => g.ToList());

        byKind["exec_confirm_waiting"].Single().GetProperty("id").GetString().ShouldBe("exec:ticket:" + _ticket.ToString("N"));
        byKind["exec_confirm_waiting"].Single().GetProperty("resolved").GetBoolean().ShouldBeFalse();
        byKind["exec_confirm_waiting"].Single().GetProperty("severity").GetString().ShouldBe("warning");
        byKind["exec_confirm_waiting"].Single().GetProperty("text").GetString()!.ShouldContain("BUY 10 SBIN");
        byKind["exec_kill_switch"].Single().GetProperty("id").GetString().ShouldBe("exec:halt:" + (_base + 1).ToString(CultureInfo.InvariantCulture));
        byKind["exec_protection_missing"].Single().GetProperty("severity").GetString().ShouldBe("error");
        byKind["exec_order_rejected"].Single().GetProperty("id").GetString().ShouldBe("exec:intent:" + _i2.ToString("N") + ":rejected");
        byKind["exec_auto_action"].Count.ShouldBe(3);
        byKind["exec_auto_action"].ShouldAllBe(i => i.GetProperty("resolved").GetBoolean() && i.GetProperty("severity").GetString() == "info");
        byKind["exec_cap_near"].Count.ShouldBe(2);
        byKind["exec_confirm_waiting"].Single().GetProperty("actions").GetArrayLength().ShouldBeLessThanOrEqualTo(2);
        items.Select(i => i.GetProperty("resolved").GetBoolean()).SequenceEqual(items.Select(i => i.GetProperty("resolved").GetBoolean()).OrderBy(r => r)).ShouldBeTrue();
    }

    [Fact]
    public async Task On_an_empty_record_every_screen_answers_empty_not_an_error()
    {
        (await GetAsync($"/api/execution/blotter?date=2026-01-05&accountId={_bare}")).GetArrayLength().ShouldBe(0);
        (await GetAsync($"/api/execution/positions?accountId={_bare}")).GetArrayLength().ShouldBe(0);
        (await GetAsync($"/api/execution/notices")).GetProperty("items").ValueKind.ShouldBe(JsonValueKind.Array);
        (await GetAsync("/api/execution/activity?date=2026-01-05")).GetArrayLength().ShouldBe(0);
    }
}
