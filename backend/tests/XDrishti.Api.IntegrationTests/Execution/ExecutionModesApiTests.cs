using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>
/// Spec P12-07 criteria 2 to 8, 11 and 18 on a real database: the modes are read with their evidence (an unreadable record is unavailable and never a pass), a promotion request and its confirmation are the owner's alone, behind Live:Writes:Enabled
/// (asked before the password) and a fresh password, nothing is written while the evidence is missing, and the owner can always step down without a password. The generic trading setting never takes an automated mode. The mode tables are insert-only, so each test has its own account.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExecutionModesApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Modes = new("/api/execution/modes", UriKind.Relative);

    private IConfigurationRoot Configuration => (IConfigurationRoot)Factory.Services.GetRequiredService<IConfiguration>();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Factory.Clock.Pin(DateTimeOffset.UtcNow); // the mode tables are stamped by the database clock, so the handlers must read the same time
        await ResignInAsync();
        Configuration["Live:Writes:Enabled"] = "true";
    }

    public override async ValueTask DisposeAsync()
    {
        Configuration["Live:Writes:Enabled"] = null;
        await base.DisposeAsync();
    }

    private async Task<Guid> AccountAsync(string mode = "ManualLive")
    {
        var id = Guid.NewGuid();
        var client = Random.Shared.NextInt64(2_000_000_000, 9_000_000_000);
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget) VALUES (@id, @slug, @name, 'Dhan', @client, current_date, '#112233', now(), now(), false, false, false, 0)",
            ("id", id), ("slug", "m-" + id.ToString("N")), ("name", "Mode " + id.ToString("N")[..8]), ("client", client.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await ExecuteAsync(connection, "INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments) VALUES (@id, true, @mode, 'Fixed', 100000, true, false, 1, 0, 0)", ("id", id), ("mode", mode));
        await ExecuteAsync(connection, "INSERT INTO accounts.portfolio_settings (account_id, enabled, include_in_totals) VALUES (@id, false, true)", ("id", id));
        return id;
    }

    private async Task<(HttpStatusCode Status, JsonElement Json)> PostAsync(Uri route, object body, HttpClient? client = null)
    {
        using var response = await (client ?? Client).PostAsJsonAsync(route, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Uri Requests(Guid account) => new($"/api/execution/modes/{account}/promotion-requests", UriKind.Relative);

    private static Uri Confirm(Guid account, Guid request) => new($"/api/execution/modes/{account}/promotion-requests/{request}/confirm", UriKind.Relative);

    private static Uri Demotions(Guid account) => new($"/api/execution/modes/{account}/demotions", UriKind.Relative);

    private async Task<JsonElement> AccountViewAsync(Guid account)
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(Modes, Ct);
        return view.GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("accountId").GetGuid() == account).Clone();
    }

    /// <summary>The account is put at <c>assisted</c> the way the owner's confirmation would: a settings row ON, a request inside its window and the promotion row.</summary>
    private async Task PromoteByHandAsync(Guid account)
    {
        var request = Guid.NewGuid();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (true, 'owner:test', 'test')");
        await ExecuteAsync(connection, "ALTER TABLE execution.promotion_requests DISABLE TRIGGER trg_exec_promotion_requests_check"); // the floor forbids the seconds-long window a test needs
        await ExecuteAsync(
            connection,
            "INSERT INTO execution.promotion_requests (id, account_id, from_mode, to_mode, requested_by, reason, eligible_from, expires_at, evidence, evidence_hash, policy_version) VALUES (@r, @a, 'manual_live', 'assisted', 'owner:test', 'r', clock_timestamp() + interval '1 second', clock_timestamp() + interval '1 hour', '{}', repeat('a', 64), 1)",
            ("r", request), ("a", account));
        await ExecuteAsync(connection, "ALTER TABLE execution.promotion_requests ENABLE TRIGGER trg_exec_promotion_requests_check");
        await Task.Delay(TimeSpan.FromMilliseconds(1300), Ct);
        await ExecuteAsync(
            connection,
            "INSERT INTO execution.mode_trail (account_id, from_mode, to_mode, kind, actor, reason, request_id, gate_snapshot, gate_hash, effective_from) VALUES (@a, 'manual_live', 'assisted', 'promotion', 'owner:test', 'r', @r, '{}', repeat('b', 64), clock_timestamp() + interval '1 second')",
            ("a", account), ("r", request));
        await Task.Delay(TimeSpan.FromMilliseconds(1300), Ct);
        Factory.Clock.Pin(DateTimeOffset.UtcNow.AddSeconds(1)); // the handlers read the same moment as the database
    }

    [Fact]
    public async Task Without_a_session_every_mode_route_is_401()
    {
        var anonymous = Factory.CreateAnonymousClient();
        var account = Guid.NewGuid();

        using var read = await anonymous.GetAsync(Modes, Ct);
        using var request = await anonymous.PostAsJsonAsync(Requests(account), new { toMode = "assisted", reason = "x", password = Factory.OwnerPassword }, Ct);
        using var confirm = await anonymous.PostAsJsonAsync(Confirm(account, Guid.NewGuid()), new { password = Factory.OwnerPassword }, Ct);
        using var demote = await anonymous.PostAsJsonAsync(Demotions(account), new { toMode = "paper", reason = "x" }, Ct);

        new[] { read, request, confirm, demote }.Select(r => r.StatusCode).ShouldAllBe(s => s == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_modes_view_shows_the_account_with_every_check_and_a_record_that_does_not_exist_yet_is_unavailable_never_a_pass()
    {
        var account = await AccountAsync();

        var view = await AccountViewAsync(account);

        view.GetProperty("configuredMode").GetString().ShouldBe("manual_live");
        view.GetProperty("effectiveMode").GetString().ShouldBe("manual_live");
        var next = view.GetProperty("next");
        next.GetProperty("to").GetString().ShouldBe("assisted");
        next.GetProperty("verdict").GetString().ShouldBe("not_eligible");
        var outcomes = next.GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("code").GetString()!, c => c.GetProperty("outcome").GetString()!);
        outcomes["sandbox_suite"].ShouldBe("unavailable");
        outcomes["static_ip_attestation"].ShouldBe("unavailable");
        outcomes["live_phase_check"].ShouldBe("unavailable");
        outcomes["trading_role"].ShouldBe("pass");
        outcomes["connection"].ShouldBe("fail");
        view.ToString().ShouldNotContain("clientId", Case.Insensitive);
        view.ToString().ShouldNotContain("token", Case.Insensitive);
    }

    [Fact]
    public async Task A_closed_gate_is_403_live_capability_off_before_the_password_is_looked_at_and_nothing_is_written()
    {
        var account = await AccountAsync();
        Configuration["Live:Writes:Enabled"] = null;

        var wrong = await PostAsync(Requests(account), new { toMode = "assisted", reason = "why", password = "not-the-password" });
        var right = await PostAsync(Requests(account), new { toMode = "assisted", reason = "why", password = Factory.OwnerPassword });
        var confirm = await PostAsync(Confirm(account, Guid.NewGuid()), new { password = Factory.OwnerPassword });

        new[] { wrong, right, confirm }.Select(r => (r.Status, r.Json.GetProperty("code").GetString())).ShouldAllBe(r => r.Status == HttpStatusCode.Forbidden && r.Item2 == "live.capability_off");
        (await ScalarAsync<long>("SELECT count(*) FROM execution.promotion_requests WHERE account_id = @a", ("a", account))).ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-password")]
    public async Task A_missing_or_wrong_password_is_401_and_nothing_is_written(string? password)
    {
        var account = await AccountAsync();

        var result = await PostAsync(Requests(account), new { toMode = "assisted", reason = "why", password });

        result.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await ScalarAsync<long>("SELECT count(*) FROM execution.promotion_requests WHERE account_id = @a", ("a", account))).ShouldBe(0);
    }

    [Fact]
    public async Task With_the_evidence_missing_the_request_is_409_with_every_reason_and_writes_nothing_and_a_step_up_window_is_not_enough()
    {
        var account = await AccountAsync();
        var elevated = await Factory.SignInElevatedAsync(Ct);

        var stepUpOnly = await PostAsync(Requests(account), new { toMode = "assisted", reason = "why" }, elevated.Client);
        var result = await PostAsync(Requests(account), new { toMode = "assisted", reason = "why", password = Factory.OwnerPassword });

        stepUpOnly.Status.ShouldBe(HttpStatusCode.Unauthorized);
        result.Status.ShouldBe(HttpStatusCode.Conflict);
        result.Json.GetProperty("code").GetString().ShouldBe("execution.mode_gate_failed");
        var detail = result.Json.GetProperty("title").GetString()!;
        detail.ShouldContain("sandbox suite");
        detail.ShouldContain("static-IP attestation");
        detail.ShouldContain("phase check");
        (await ScalarAsync<long>("SELECT count(*) FROM execution.promotion_requests WHERE account_id = @a", ("a", account))).ShouldBe(0);
    }

    [Theory]
    [InlineData("semi_auto")]
    [InlineData("auto_live")]
    [InlineData("nonsense")]
    public async Task The_automated_modes_are_400_values_invalid_and_a_skip_is_409_not_adjacent(string mode)
    {
        var account = await AccountAsync();
        var paper = await AccountAsync("Paper");

        var automated = await PostAsync(Requests(account), new { toMode = mode, reason = "why", password = Factory.OwnerPassword });
        var skip = await PostAsync(Requests(paper), new { toMode = "assisted", reason = "why", password = Factory.OwnerPassword });

        automated.Status.ShouldBe(HttpStatusCode.BadRequest);
        automated.Json.GetProperty("code").GetString().ShouldBe("execution.values_invalid");
        skip.Status.ShouldBe(HttpStatusCode.Conflict);
        skip.Json.GetProperty("code").GetString().ShouldBe("execution.mode_not_adjacent");
    }

    [Fact]
    public async Task The_owner_steps_down_at_once_without_a_password_or_the_gate_with_the_actor_from_the_session_and_cannot_step_up_that_way()
    {
        var account = await AccountAsync();
        await PromoteByHandAsync(account);
        Configuration["Live:Writes:Enabled"] = null; // stepping down needs no gate
        (await AccountViewAsync(account)).GetProperty("effectiveMode").GetString().ShouldBe("assisted");

        var up = await PostAsync(Demotions(account), new { toMode = "assisted", reason = "same" });
        var automated = await PostAsync(Demotions(account), new { toMode = "semi_auto", reason = "up" });
        var down = await PostAsync(Demotions(account), new { toMode = "paper", reason = "gap down day", actor = "owner:evil@example.test" });
        var again = await PostAsync(Demotions(account), new { toMode = "paper", reason = "once more" });

        up.Json.GetProperty("code").GetString().ShouldBe("execution.mode_not_lower");
        automated.Json.GetProperty("code").GetString().ShouldBe("execution.values_invalid");
        down.Status.ShouldBe(HttpStatusCode.OK);
        down.Json.GetProperty("kind").GetString().ShouldBe("owner_demotion");
        down.Json.GetProperty("actor").GetString().ShouldBe("owner:" + ApiFactory.OwnerEmail);
        again.Json.GetProperty("code").GetString().ShouldBe("execution.mode_not_lower");
        (await AccountViewAsync(account)).GetProperty("effectiveMode").GetString().ShouldBe("paper");
        (await ScalarAsync<string>("SELECT actor FROM execution.mode_trail WHERE account_id = @a AND kind = 'owner_demotion'", ("a", account))).ShouldBe("owner:" + ApiFactory.OwnerEmail);
    }

    [Fact]
    public async Task The_generic_trading_setting_never_takes_an_assisted_or_automated_mode()
    {
        var account = await AccountAsync("Paper");
        var profile = await ScalarAsync<Guid>("SELECT id FROM accounts.risk_profiles LIMIT 1");
        var elevated = await Factory.SignInElevatedAsync(Ct);

        foreach (var mode in new[] { "assisted", "semi_auto", "auto_live" })
        {
            var body = new { mode, riskProfileId = profile, capitalSource = "fixed", capitalAmount = 500000, styles = new[] { "intraday" }, instruments = new { intraday = new[] { "EQ_MIS" } } };
            using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/accounts/{account}/trading", UriKind.Relative)) { Content = JsonContent.Create(body) };
            using var response = await elevated.Client.SendAsync(request, Ct);
            var text = await response.Content.ReadAsStringAsync(Ct);

            ((int)response.StatusCode).ShouldBeOneOf(400, 422);
            text.ShouldContain("mode");
        }

        (await ScalarAsync<string>("SELECT mode FROM accounts.trading_settings WHERE account_id = @a", ("a", account))).ShouldBe("Paper");
    }

    [Fact]
    public async Task There_is_no_route_that_edits_or_deletes_a_mode_and_none_that_names_an_order()
    {
        var account = Guid.NewGuid();
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            using var request = new HttpRequestMessage(method, new Uri($"/api/execution/modes/{account}", UriKind.Relative));
            using var response = await Client.SendAsync(request, Ct);
            ((int)response.StatusCode).ShouldBeOneOf(404, 405);
        }

        using var sheets = await Client.GetAsync(new Uri("/api/execution/sheets", UriKind.Relative), Ct);
        ((int)sheets.StatusCode).ShouldBeOneOf(404, 405); // the assisted sheet is P12-07b
    }
}
