using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>
/// ADR 0174 over HTTP on a real database: the global switches and the kill-switch drill are read with a session and written only by the owner with a fresh password; switching execution on waits for a full closed session behind the live-writes
/// gate; switching off is in force at once; auto_live is never stored; a drill request is a command row that carries nothing. The settings record is insert-only, so each test first writes the state it needs.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExecutionSwitchApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Settings = new("/api/execution/settings", UriKind.Relative);
    private static readonly Uri Drills = new("/api/execution/drills", UriKind.Relative);

    private IConfigurationRoot Configuration => (IConfigurationRoot)Factory.Services.GetRequiredService<IConfiguration>();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await ResignInAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        Configuration["Live:Writes:Enabled"] = null;
        await base.DisposeAsync();
    }

    private async Task<(HttpStatusCode Status, JsonElement Json)> SendAsync(HttpMethod method, Uri route, object body, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(method, route) { Content = JsonContent.Create(body) };
        using var response = await (client ?? Client).SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<(HttpStatusCode Status, JsonElement Json)> PutAsync(bool on, string? password, bool autoLive = false, HttpClient? client = null) =>
        SendAsync(HttpMethod.Put, Settings, new { executionEnabled = on, autoLiveEnabled = autoLive, reason = "the owner's switch", password }, client);

    private async Task<Guid> AccountAsync(bool trading = true)
    {
        var id = Guid.NewGuid();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget) VALUES (@id, @slug, @name, 'Dhan', @client, current_date, '#112233', now(), now(), false, false, false, 0)",
            ("id", id), ("slug", "d-" + id.ToString("N")), ("name", "Drill " + id.ToString("N")[..8]), ("client", Random.Shared.NextInt64(2_000_000_000, 9_000_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (!trading)
        {
            return id;
        }

        await ExecuteAsync(connection, "INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments) VALUES (@id, true, 'ManualLive', 'Fixed', 100000, true, false, 1, 0, 0)", ("id", id));
        await ExecuteAsync(connection, "INSERT INTO accounts.portfolio_settings (account_id, enabled, include_in_totals) VALUES (@id, false, true)", ("id", id));
        return id;
    }

    [Fact]
    public async Task Without_a_session_every_switch_and_drill_route_is_401()
    {
        var anonymous = Factory.CreateAnonymousClient();

        using var settings = await anonymous.GetAsync(Settings, Ct);
        using var drills = await anonymous.GetAsync(Drills, Ct);
        var put = await PutAsync(false, Factory.OwnerPassword, client: anonymous);
        var post = await SendAsync(HttpMethod.Post, Drills, new { accountId = Guid.NewGuid(), password = Factory.OwnerPassword }, anonymous);

        settings.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        drills.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        put.Status.ShouldBe(HttpStatusCode.Unauthorized);
        post.Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Switching_execution_on_needs_the_gate_before_the_password_then_waits_for_09_00_after_a_full_closed_session_and_switching_off_cancels_it()
    {
        (await PutAsync(false, Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);

        var gateOff = await PutAsync(true, "not the password");
        Configuration["Live:Writes:Enabled"] = "true";
        var wrong = await PutAsync(true, "not the password");
        var raised = await PutAsync(true, Factory.OwnerPassword);
        var lowered = await PutAsync(false, Factory.OwnerPassword);

        gateOff.Status.ShouldBe(HttpStatusCode.Forbidden);
        gateOff.Json.GetProperty("code").GetString().ShouldBe("live.capability_off");
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        raised.Status.ShouldBe(HttpStatusCode.OK);
        raised.Json.GetProperty("inForce").GetProperty("executionEnabled").GetBoolean().ShouldBeFalse();
        var effective = raised.Json.GetProperty("pending").GetProperty("effectiveFrom").GetDateTimeOffset().ToOffset(TimeSpan.FromMinutes(330));
        effective.TimeOfDay.ShouldBe(new TimeSpan(9, 0, 0));
        effective.ShouldBeGreaterThan(raised.Json.GetProperty("serverNow").GetDateTimeOffset().AddHours(12));
        raised.Json.GetProperty("autoLiveLocked").GetBoolean().ShouldBeTrue();
        lowered.Status.ShouldBe(HttpStatusCode.OK);
        lowered.Json.GetProperty("inForce").GetProperty("executionEnabled").GetBoolean().ShouldBeFalse();
        lowered.Json.GetProperty("pending").ValueKind.ShouldBe(JsonValueKind.Null);
        (await ScalarAsync<string>("SELECT event FROM accounts.account_events WHERE event = 'ExecutionSettingsChanged' ORDER BY at DESC LIMIT 1")).ShouldBe("ExecutionSettingsChanged");
    }

    [Fact]
    public async Task Every_change_needs_the_password_and_auto_live_on_is_always_409()
    {
        Configuration["Live:Writes:Enabled"] = "true";
        var before = await ScalarAsync<long>("SELECT count(*) FROM execution.settings");

        var withoutPassword = await PutAsync(false, null);
        var autoLive = await PutAsync(true, Factory.OwnerPassword, autoLive: true);

        withoutPassword.Status.ShouldBe(HttpStatusCode.BadRequest);
        withoutPassword.Json.GetProperty("code").GetString().ShouldBe("execution.password_required");
        autoLive.Status.ShouldBe(HttpStatusCode.Conflict);
        autoLive.Json.GetProperty("code").GetString().ShouldBe("execution.auto_live_locked");
        (await ScalarAsync<long>("SELECT count(*) FROM execution.settings")).ShouldBe(before);
        (await ScalarAsync<long>("SELECT count(*) FROM execution.settings WHERE auto_live_enabled")).ShouldBe(0);
    }

    [Fact]
    public async Task A_drill_needs_a_fresh_password_and_a_listed_account_and_leaves_a_drill_command_that_carries_nothing()
    {
        var account = await AccountAsync();
        var halted = await ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM execution.halts h WHERE NOT EXISTS (SELECT 1 FROM execution.halt_releases r WHERE r.halt_id = h.id) AND NOT EXISTS (SELECT 1 FROM execution.halt_auto_clears c WHERE c.halt_id = h.id))");

        var wrong = await SendAsync(HttpMethod.Post, Drills, new { accountId = account, password = "not the password" });
        var unknown = await SendAsync(HttpMethod.Post, Drills, new { accountId = Guid.NewGuid(), password = Factory.OwnerPassword });
        var started = await SendAsync(HttpMethod.Post, Drills, new { accountId = account, password = Factory.OwnerPassword });
        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution/drills?accountId={account}", UriKind.Relative), Ct);

        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        view.GetProperty("maxAgeDays").GetInt32().ShouldBe(30);
        view.GetProperty("drills").GetArrayLength().ShouldBe(0);
        if (halted)
        {
            // A halt of another test is still in force in the shared database: the drill must refuse beside it.
            started.Status.ShouldBe(HttpStatusCode.Conflict);
            started.Json.GetProperty("code").GetString().ShouldBe("execution.drill_refused");
            view.GetProperty("requests").GetArrayLength().ShouldBe(0);
            return;
        }

        started.Status.ShouldBe(HttpStatusCode.OK);
        var id = started.Json.GetProperty("commandId").GetGuid();
        var request = view.GetProperty("requests").EnumerateArray().ShouldHaveSingleItem();
        request.GetProperty("commandId").GetGuid().ShouldBe(id);
        request.GetProperty("result").ValueKind.ShouldBe(JsonValueKind.Null);
        (await ScalarAsync<string>("SELECT kind || '|' || payload::text || '|' || account_id::text FROM execution.commands WHERE id = @id", ("id", id))).ShouldBe($"drill|{{}}|{account}");
    }

    [Fact]
    public async Task An_account_without_a_trading_row_does_not_break_the_drill_or_the_status()
    {
        var bare = await AccountAsync(trading: false);

        var drill = await SendAsync(HttpMethod.Post, Drills, new { accountId = bare, password = Factory.OwnerPassword });
        using var status = await Client.GetAsync(new Uri("/api/execution/status", UriKind.Relative), Ct);

        ((int)drill.Status).ShouldBeOneOf(200, 409); // 409 only while another test's halt is in force
        status.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_drill_is_409_while_a_halt_is_in_force()
    {
        var account = await AccountAsync();
        var halt = await ScalarAsync<long>("INSERT INTO execution.halts (at, scope, account_id, level, trigger_code, source, actor, reason) VALUES (now(), 'account', @a, 'entries', 'owner_button', 'owner', 'owner:t', 'drill test') RETURNING id", ("a", account));

        var refused = await SendAsync(HttpMethod.Post, Drills, new { accountId = account, password = Factory.OwnerPassword });
        await ScalarAsync<long>("INSERT INTO execution.halt_releases (halt_id, released_by, reason) VALUES (@h, 'owner:t', 'drill test over') RETURNING halt_id", ("h", halt)); // leave no halt in force for the other tests

        refused.Status.ShouldBe(HttpStatusCode.Conflict);
        refused.Json.GetProperty("code").GetString().ShouldBe("execution.drill_refused");
        (await ScalarAsync<long>("SELECT count(*) FROM execution.commands WHERE kind = 'drill' AND account_id = @a", ("a", account))).ShouldBe(0);
    }
}
