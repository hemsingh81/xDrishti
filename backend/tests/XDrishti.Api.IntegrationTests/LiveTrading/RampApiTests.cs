using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.LiveTrading;

/// <summary>
/// P11-04a on a real database: the ramp step and the live switches are owner acts behind the gate and a fresh password; the tables refuse what spec criterion 6 lists; the allocation row, the live-settings guard on an automatic approval, the account mode gate,
/// and everything defaults OFF.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class RampApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid AccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a31");

    private static readonly Uri Steps = new("/api/live-trading/ramp/steps", UriKind.Relative);

    private static readonly Uri Settings = new("/api/live-trading/settings", UriKind.Relative);

    private IConfigurationRoot Configuration => (IConfigurationRoot)Factory.Services.GetRequiredService<IConfiguration>();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Configuration["Live:Writes:Enabled"] = "true";
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES (@id, 'probe-ramp', 'Probe', 'Dhan', '1000000091', current_date, '#1A2B3C', now(), now(), false, false, false, 200)
            """,
            ("id", AccountId));
        await ExecuteAsync(connection, "INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments) VALUES (@id, true, 'Paper', 'Fixed', 100000, true, false, 1, 0, 0)", ("id", AccountId));
        await ExecuteAsync(connection, "INSERT INTO accounts.portfolio_settings (account_id, enabled, include_in_totals) VALUES (@id, false, true)", ("id", AccountId));
    }

    public override async ValueTask DisposeAsync()
    {
        Configuration["Live:Writes:Enabled"] = null;
        await base.DisposeAsync();
    }

    private async Task<(HttpStatusCode Status, JsonElement Json)> SendAsync(HttpMethod method, Uri route, object body)
    {
        using var request = new HttpRequestMessage(method, route) { Content = JsonContent.Create(body) };
        using var response = await Client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<(HttpStatusCode Status, JsonElement Json)> StepAsync(int to, string? password, string reason = "first step") =>
        SendAsync(HttpMethod.Post, Steps, new { accountId = AccountId, toStep = to, reason, password });

    private async Task<string?> SqlStateAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        try
        {
            await command.ExecuteNonQueryAsync(Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    private const string InsertEvent = "INSERT INTO trading.ramp_events (account_id, kind, from_step, to_step, hold, actor, reason, settings_id, settings_hash) VALUES (@a, @kind, @from, @to, @hold, @actor, 'r', NULL, NULL)";

    private Task<string?> EventAsync(string kind, int? from, int to, string actor, bool hold = false) =>
        SqlStateAsync(InsertEvent, ("a", AccountId), ("kind", kind), ("from", (object?)from ?? DBNull.Value), ("to", to), ("hold", hold), ("actor", actor));

    [Fact]
    public async Task With_nothing_stored_the_ramp_has_no_step_and_both_switches_read_off()
    {
        var ramp = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/live-trading/ramp?accountId={AccountId}", UriKind.Relative), Ct);
        var settings = await Client.GetFromJsonAsync<JsonElement>(Settings, Ct);

        ramp.GetProperty("step").ValueKind.ShouldBe(JsonValueKind.Null);
        ramp.GetProperty("gatesBuilt").GetBoolean().ShouldBeFalse();
        ramp.GetProperty("eligible").GetBoolean().ShouldBeFalse();
        settings.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null);
        settings.GetProperty("autoApproveTickets").GetBoolean().ShouldBeFalse();
        settings.GetProperty("autoRampUp").GetBoolean().ShouldBeFalse();
        using var unknown = await Client.GetAsync(new Uri($"/api/live-trading/ramp?accountId={Guid.NewGuid()}", UriKind.Relative), Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_gate_comes_first_then_the_fresh_password_and_a_raise_is_refused_until_the_gates_exist()
    {
        Configuration["Live:Writes:Enabled"] = null;
        var closed = await StepAsync(1, Factory.OwnerPassword);
        Configuration["Live:Writes:Enabled"] = "true";
        var noPassword = await StepAsync(1, null);
        var wrong = await StepAsync(1, "not-the-password");
        var ok = await StepAsync(1, Factory.OwnerPassword);
        var raise = await StepAsync(2, Factory.OwnerPassword, "up");
        var skip = await StepAsync(3, Factory.OwnerPassword, "up");

        closed.Json.GetProperty("code").GetString().ShouldBe("live.capability_off");
        noPassword.Status.ShouldBe(HttpStatusCode.Unauthorized);
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        ok.Status.ShouldBe(HttpStatusCode.OK);
        ok.Json.GetProperty("step").GetInt32().ShouldBe(1);
        ok.Json.GetProperty("scaleEffective").GetDouble().ShouldBe(0.25);
        raise.Json.GetProperty("code").GetString().ShouldBe("live.gates_not_met");
        skip.Json.GetProperty("code").GetString().ShouldBe("live.step_not_adjacent");
        (await ScalarAsync<long>("SELECT count(*) FROM trading.ramp_events")).ShouldBe(1);
        (await ScalarAsync<string>("SELECT actor FROM trading.ramp_events")).ShouldStartWith("owner:");
        (await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'RampStepSet'")).ShouldBe("event=" + await ScalarAsync<long>("SELECT id FROM trading.ramp_events") + " from=none to=1");
    }

    [Fact]
    public async Task The_switches_are_changed_by_the_owner_with_the_gate_and_a_password_and_the_database_stamps_the_hash()
    {
        var wrong = await SendAsync(HttpMethod.Put, Settings, new { autoApproveTickets = true, autoRampUp = false, reason = "x", expectedSettingsId = 0, password = "nope" });
        var on = await SendAsync(HttpMethod.Put, Settings, new { autoApproveTickets = true, autoRampUp = false, reason = "try it", expectedSettingsId = 0, password = Factory.OwnerPassword });
        var stale = await SendAsync(HttpMethod.Put, Settings, new { autoApproveTickets = false, autoRampUp = false, reason = "off", expectedSettingsId = 0, password = Factory.OwnerPassword });

        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        on.Status.ShouldBe(HttpStatusCode.OK);
        on.Json.GetProperty("autoApproveTickets").GetBoolean().ShouldBeTrue();
        stale.Json.GetProperty("code").GetString().ShouldBe("live.stale");
        (await ScalarAsync<string>("SELECT snapshot_hash FROM trading.live_settings")).Trim().Length.ShouldBe(64);
        (await SqlStateAsync("INSERT INTO trading.live_settings (auto_approve_tickets, auto_ramp_up, actor, reason) VALUES (true, false, 'rule:ramp', 'x')")).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_ramp_table_refuses_what_criterion_6_lists()
    {
        // the owner kinds need an owner actor, the rule kinds the rule
        (await EventAsync("set_step", null, 1, "rule:ramp")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await EventAsync("hold", null, 1, "owner:me@example.invalid")).ShouldBe(PostgresErrorCodes.CheckViolation);
        // the first event is S1 by the owner; a first S2 is a skip
        (await EventAsync("set_step", null, 2, "owner:me@example.invalid")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await EventAsync("set_step", null, 1, "owner:me@example.invalid")).ShouldBeNull();
        // no skip upwards, no S3 without the owner's exit confirmation, a stale from-step is refused
        (await EventAsync("set_step", 1, 3, "owner:me@example.invalid")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await EventAsync("set_step", 2, 3, "owner:me@example.invalid")).ShouldBe(PostgresErrorCodes.CheckViolation);
        // an automatic step-up needs the rule and a settings snapshot, and never goes above step 2
        (await EventAsync("auto_step_up", 1, 2, "rule:ramp")).ShouldBe(PostgresErrorCodes.CheckViolation);
        // a hold by the rule, then raising is blocked, clearing is the owner's and needs the hold
        (await EventAsync("hold", 1, 1, "rule:ramp", hold: true)).ShouldBeNull();
        (await EventAsync("set_step", 1, 2, "owner:me@example.invalid", hold: true)).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await EventAsync("clear_hold", 1, 1, "rule:ramp")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await EventAsync("clear_hold", 1, 1, "owner:me@example.invalid")).ShouldBeNull();
        (await EventAsync("clear_hold", 1, 1, "owner:me@example.invalid")).ShouldBe(PostgresErrorCodes.CheckViolation);
        // the owner raises one step, confirms the exit, and only then S3 is possible; the owner may lower to any lower step
        (await EventAsync("set_step", 1, 2, "owner:me@example.invalid")).ShouldBeNull();
        (await EventAsync("set_step", 2, 3, "owner:me@example.invalid")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await EventAsync("exit_confirmed", 2, 2, "owner:me@example.invalid")).ShouldBeNull();
        (await EventAsync("set_step", 2, 3, "owner:me@example.invalid")).ShouldBeNull();
        (await EventAsync("set_step", 3, 1, "owner:me@example.invalid")).ShouldBeNull();
        (await EventAsync("step_down", 1, 1, "rule:ramp")).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_live_allocation_needs_its_scale_and_a_ramp_event_of_its_own_account_below_the_ceiling_and_the_auto_approval_needs_the_live_snapshot()
    {
        (await EventAsync("set_step", null, 1, "owner:me@example.invalid")).ShouldBeNull();
        var ev = await ScalarAsync<long>("SELECT max(id) FROM trading.ramp_events");
        var item = await PlanItemAsync();

        const string Insert = "INSERT INTO trading.plan_item_allocations (item_id, account_id, mode, quantity, risk_scale, ramp_event_id) VALUES (@i, @a, @m, 5, @s, @e)";
        (await SqlStateAsync(Insert, ("i", item), ("a", AccountId), ("m", "manual_live"), ("s", DBNull.Value), ("e", DBNull.Value))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(Insert, ("i", item), ("a", AccountId), ("m", "manual_live"), ("s", 1.5m), ("e", ev))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(Insert, ("i", item), ("a", AccountId), ("m", "manual_live"), ("s", 0.5m), ("e", ev))).ShouldBe(PostgresErrorCodes.CheckViolation); // above the S1 ceiling of 0.25
        (await SqlStateAsync(Insert, ("i", item), ("a", AccountId), ("m", "paper"), ("s", 0.25m), ("e", ev))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(Insert, ("i", item), ("a", Guid.NewGuid()), ("m", "manual_live"), ("s", 0.25m), ("e", ev))).ShouldNotBeNull();
        (await SqlStateAsync(Insert, ("i", item), ("a", AccountId), ("m", "manual_live"), ("s", 0.25m), ("e", ev))).ShouldBeNull();

        // an automatic approval of this ticket: the autonomy switch alone is not enough
        await SqlStateAsync("INSERT INTO workflow.autonomy_settings (auto_approve_tickets, promote_from_reserve, min_p_win, max_auto_per_day, actor, reason) VALUES (true, false, 0.58, 3, 'owner:me@example.invalid', 'r')");
        var auto = await ScalarAsync<long>("SELECT max(id) FROM workflow.autonomy_settings");
        var hash = await ScalarAsync<string>("SELECT snapshot_hash FROM workflow.autonomy_settings ORDER BY id DESC LIMIT 1");
        const string InsertAuto = """
            INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, cutoff, decided_at, settings_id, settings_hash, policy_version, live_settings_id, live_settings_hash)
            SELECT p.id, @item, 'auto', 'approve', 'rule:autonomy', 'r', p.review_cutoff, now(), @sid, @sh, 'auto-approval/2', @lid, @lh FROM trading.plans p JOIN trading.plan_items i ON i.plan_id = p.id WHERE i.id = @item
            """;
        (await SqlStateAsync(InsertAuto, ("item", item), ("sid", auto), ("sh", hash), ("lid", DBNull.Value), ("lh", DBNull.Value))).ShouldBe(PostgresErrorCodes.CheckViolation);

        // live switch OFF in a stored row: still refused; ON in the newest row: allowed
        await SqlStateAsync("INSERT INTO trading.live_settings (auto_approve_tickets, auto_ramp_up, actor, reason) VALUES (false, false, 'owner:me@example.invalid', 'r')");
        var off = await ScalarAsync<long>("SELECT max(id) FROM trading.live_settings");
        var offHash = await ScalarAsync<string>("SELECT snapshot_hash FROM trading.live_settings WHERE id = " + off);
        (await SqlStateAsync(InsertAuto, ("item", item), ("sid", auto), ("sh", hash), ("lid", off), ("lh", offHash))).ShouldBe(PostgresErrorCodes.CheckViolation);
        await SqlStateAsync("INSERT INTO trading.live_settings (auto_approve_tickets, auto_ramp_up, actor, reason) VALUES (true, false, 'owner:me@example.invalid', 'r')");
        var on = await ScalarAsync<long>("SELECT max(id) FROM trading.live_settings");
        var onHash = await ScalarAsync<string>("SELECT snapshot_hash FROM trading.live_settings WHERE id = " + on);
        (await SqlStateAsync(InsertAuto, ("item", item), ("sid", auto), ("sh", hash), ("lid", off), ("lh", offHash))).ShouldBe(PostgresErrorCodes.CheckViolation); // not the newest row
        (await SqlStateAsync(InsertAuto, ("item", item), ("sid", auto), ("sh", hash), ("lid", on), ("lh", "x" + onHash[1..]))).ShouldNotBeNull(); // hash must match the row
        (await SqlStateAsync(InsertAuto, ("item", item), ("sid", auto), ("sh", hash), ("lid", on), ("lh", onHash))).ShouldBeNull();
    }

    [Fact]
    public async Task The_switch_to_manual_live_is_refused_by_the_endpoint_with_every_failing_item()
    {
        var profile = await ScalarAsync<Guid>("SELECT id FROM accounts.risk_profiles LIMIT 1");
        var body = new { mode = "manual_live", riskProfileId = profile, capitalSource = "fixed", capitalAmount = 500000, styles = new[] { "intraday" }, instruments = new { intraday = new[] { "EQ_MIS" } } };
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/accounts/{AccountId}/trading", UriKind.Relative)) { Content = JsonContent.Create(body) };
        var elevated = await Factory.SignInElevatedAsync(Ct);
        using var response = await elevated.Client.SendAsync(request, Ct);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        json.GetProperty("code").GetString().ShouldBe("accounts.live_not_ready");
        json.GetProperty("errors").ToString().ShouldContain("ramp_step");
        (await ScalarAsync<string>("SELECT mode FROM accounts.trading_settings WHERE account_id = '" + AccountId + "'")).ShouldBe("Paper");
    }

    private async Task<long> PlanItemAsync()
    {
        var published = await Plans(p => p.PublishAsync(
            new NewPlan("ramp-probe", new DateOnly(2099, 1, 5), new DateTimeOffset(2099, 1, 5, 3, 35, 0, TimeSpan.Zero), 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch,
            [new NewPlanItem(1, TicketGroup.Primary, "NSE:RELIANCE", "BUY", "ORB", new string('a', 64), "t", "s", "2R", "v", [], new PlanEvidence("c", 41, "d", "b", 0.7, 0.1, [], null, 0.7), [])]), Ct).AsTask());
        var items = await Plans(p => p.ItemsAsync(published.Id, Ct).AsTask());
        return items[0].Id;
    }

    private async Task<TResult> Plans<TResult>(Func<IPlanStore, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<IPlanStore>());
    }
}
