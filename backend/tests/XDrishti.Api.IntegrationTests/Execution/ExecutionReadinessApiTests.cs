using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>
/// Spec P12-11 criteria 9, 11 and 12 on a real database: the readiness is read per account from the stored checks, day states and attestation (nothing recorded is unavailable, never ready), an attestation is the owner's alone with the actor from the session and the time from
/// the database, it never contacts the broker, and check now leaves one command row. The tables are insert-only, so each test has its own account.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExecutionReadinessApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Readiness = new("/api/execution/readiness", UriKind.Relative);

    private static readonly Uri Attestations = new("/api/execution/ip-attestations", UriKind.Relative);

    private static readonly Uri CheckNow = new("/api/execution/readiness/checks", UriKind.Relative);

    private const string Session = "2026-10-27";

    private async Task<(Guid Id, string Client)> AccountAsync()
    {
        var id = Guid.NewGuid();
        var client = Random.Shared.NextInt64(2_000_000_000, 9_000_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget) VALUES (@id, @slug, @name, 'Dhan', @client, current_date, '#112233', now(), now(), false, false, false, 0)",
            ("id", id), ("slug", "r-" + id.ToString("N")), ("name", "Ready " + id.ToString("N")[..8]), ("client", client));
        return (id, client);
    }

    private async Task CheckAsync(Guid account, string verdict, string at, string reasons, string? modifyAfter = null)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            $"INSERT INTO execution.readiness_checks (at, account_id, session_date, verdict, reasons, token_expires_at, profile_read_at, ip_read_at, ip_modify_after) VALUES ('{at}', @a, '{Session}', @v, @r::jsonb, '2026-10-27T17:30:00+05:30', '{at}', '{at}', {(modifyAfter is null ? "NULL" : $"'{modifyAfter}'")})",
            ("a", account), ("v", verdict), ("r", reasons));
    }

    private async Task<JsonElement> RowAsync(Guid account, string query = "")
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri(Readiness + query, UriKind.Relative), Ct);
        return view.GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("accountId").GetGuid() == account).Clone();
    }

    [Fact]
    public async Task Without_a_session_every_readiness_route_is_401()
    {
        var anonymous = Factory.CreateAnonymousClient();

        using var read = await anonymous.GetAsync(Readiness, Ct);
        using var attest = await anonymous.PostAsJsonAsync(Attestations, new { accountId = Guid.NewGuid(), ip = "203.0.113.7", ordersAllowed = true, note = "" }, Ct);
        using var now = await anonymous.PostAsJsonAsync(CheckNow, new { }, Ct);

        (read.StatusCode, attest.StatusCode, now.StatusCode).ShouldBe((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task With_nothing_recorded_the_view_is_empty_for_a_fresh_account_and_a_bad_date_is_a_400()
    {
        var (account, _) = await AccountAsync();

        var view = await Client.GetFromJsonAsync<JsonElement>(Readiness, Ct);
        using var bad = await Client.GetAsync(new Uri(Readiness + "?date=not-a-date", UriKind.Relative), Ct);

        view.GetProperty("accounts").EnumerateArray().ShouldNotContain(a => a.GetProperty("accountId").GetGuid() == account);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_recorded_week_reads_back_the_newest_state_its_checks_the_ready_run_the_ip_lock_the_attestation_and_the_day_states()
    {
        var (account, client) = await AccountAsync();
        await CheckAsync(account, "not_ready", "2026-10-27T09:00:00+05:30", """[{"code":"connected","outcome":"fail","detail":"not_connected","text":"Dhan is not connected: connect it.","ageSeconds":null,"since":null}]""");
        await CheckAsync(account, "ready", "2026-10-27T09:12:00+05:30", """[{"code":"connected","outcome":"pass","detail":"connected","text":"Dhan is connected.","ageSeconds":3,"since":null}]""", "2026-10-08");
        await CheckAsync(account, "ready", "2026-10-27T09:30:00+05:30", """[{"code":"connected","outcome":"pass","detail":"connected","text":"Dhan is connected.","ageSeconds":4,"since":null},{"code":"ip_attested","outcome":"pass","detail":"ip_attested","text":"The IP is attested.","ageSeconds":90000,"since":null}]""", "2026-10-08");
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO execution.day_states (account_id, session_date, state, reasons, late, at) VALUES (@a, '2026-10-26', 'unavailable', '[{\"code\":\"probe\",\"outcome\":\"unavailable\",\"detail\":\"late_start\",\"text\":\"x\"}]', true, now()), (@a, '2026-10-27', 'not_ready', '[{\"code\":\"connected\",\"outcome\":\"fail\",\"detail\":\"not_connected\",\"text\":\"x\"}]', false, now())", ("a", account));
            await ExecuteAsync(connection, "INSERT INTO execution.ip_attestations (account_id, ip, orders_allowed, note, actor) VALUES (@a, '203.0.113.7', true, 'sandbox tool says yes', 'owner:hem@example.invalid')", ("a", account));
        }

        var row = await RowAsync(account, $"?date={Session}");

        row.GetProperty("state").GetString().ShouldBe("ready");
        row.GetProperty("checks").EnumerateArray().Select(c => (c.GetProperty("code").GetString(), c.GetProperty("outcome").GetString())).ShouldBe([("connected", "pass"), ("ip_attested", "pass")]);
        row.GetProperty("checks")[1].GetProperty("ageSeconds").GetInt32().ShouldBe(90000);
        DateTimeOffset.Parse(row.GetProperty("readyAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(DateTimeOffset.Parse("2026-10-27T09:12:00+05:30", System.Globalization.CultureInfo.InvariantCulture));
        row.GetProperty("ipModifyAfter").GetString().ShouldBe("2026-10-08");
        row.GetProperty("tokenExpiresAt").ValueKind.ShouldBe(JsonValueKind.String);
        row.GetProperty("attestation").GetProperty("ordersAllowed").GetBoolean().ShouldBeTrue();
        row.GetProperty("dayStates").EnumerateArray().Select(d => (d.GetProperty("sessionDate").GetString(), d.GetProperty("state").GetString(), d.GetProperty("late").GetBoolean())).ShouldBe([("2026-10-27", "not_ready", false), ("2026-10-26", "unavailable", true)]);
        row.GetProperty("dayStates")[0].GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ShouldBe(["connected"]);
        row.GetProperty("sendsPerSecondMax").ValueKind.ShouldBe(JsonValueKind.Null); // broker_calls is insert-only with a foreign key to the account, so a row there would block the reset of every later test; the query itself is proven in ExecReadinessTests
        row.GetProperty("lastRecheck").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetRawText().ShouldNotContain(client);
    }

    [Fact]
    public async Task A_date_with_no_check_reads_unavailable_never_ready_and_a_newer_not_ready_breaks_the_ready_run()
    {
        var (account, _) = await AccountAsync();
        await CheckAsync(account, "ready", "2026-10-27T09:12:00+05:30", "[]");
        await CheckAsync(account, "not_ready", "2026-10-27T09:40:00+05:30", """[{"code":"ip_rejected","outcome":"fail","detail":"ip_rejected","text":"x"}]""");

        var other = await RowAsync(account, "?date=2026-10-20");
        var broken = await RowAsync(account, $"?date={Session}");

        (other.GetProperty("state").GetString(), other.GetProperty("checks").GetArrayLength()).ShouldBe(("unavailable", 0));
        broken.GetProperty("state").GetString().ShouldBe("not_ready");
        broken.GetProperty("readyAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_attestation_is_stored_with_the_actor_from_the_session_and_the_database_time_and_ordersAllowed_false_is_kept()
    {
        var (account, _) = await AccountAsync();

        using var yes = await Client.PostAsJsonAsync(Attestations, new { accountId = account, ip = "2001:0db8:0000:0000:0000:0000:0000:0001", ordersAllowed = true, note = " checked ", actor = "owner:evil@example.test", at = "2000-01-01T00:00:00Z" }, Ct);
        using var no = await Client.PostAsJsonAsync(Attestations, new { accountId = account, ip = "203.0.113.7", ordersAllowed = false, note = "" }, Ct);

        yes.StatusCode.ShouldBe(HttpStatusCode.OK);
        no.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await yes.Content.ReadFromJsonAsync<JsonElement>(Ct));
        body.GetProperty("ip").GetString().ShouldBe("2001:db8::1");
        body.GetProperty("note").GetString().ShouldBe("checked");
        DateTimeOffset.Parse(body.GetProperty("at").GetString()!, System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        (await ScalarAsync<long>("SELECT count(*) FROM execution.ip_attestations WHERE account_id = @a AND actor LIKE 'owner:%' AND actor <> 'owner:evil@example.test'", ("a", account))).ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(*) FROM execution.ip_attestations WHERE account_id = @a AND orders_allowed = false", ("a", account))).ShouldBe(1);
        var row = await RowAsync(account);
        row.GetProperty("attestation").GetProperty("ordersAllowed").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData("not an ip", "ok")]
    [InlineData("203.0.113.0/24", "ok")]
    [InlineData("203.0.113.7", "<b>x</b>")]
    public async Task A_bad_address_or_note_is_a_400_with_the_problem_code_and_stores_nothing(string ip, string note)
    {
        var (account, _) = await AccountAsync();

        using var response = await Client.PostAsJsonAsync(Attestations, new { accountId = account, ip, ordersAllowed = true, note }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("execution.values_invalid");
        (await ScalarAsync<long>("SELECT count(*) FROM execution.ip_attestations WHERE account_id = @a", ("a", account))).ShouldBe(0);
    }

    [Fact]
    public async Task A_note_of_201_characters_is_a_400_and_an_unknown_account_is_a_404()
    {
        var (account, _) = await AccountAsync();

        using var tooLong = await Client.PostAsJsonAsync(Attestations, new { accountId = account, ip = "203.0.113.7", ordersAllowed = true, note = new string('x', 201) }, Ct);
        using var unknown = await Client.PostAsJsonAsync(Attestations, new { accountId = Guid.NewGuid(), ip = "203.0.113.7", ordersAllowed = true, note = "" }, Ct);

        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.Content.ReadAsStringAsync(Ct)).ShouldContain("execution.not_found");
    }

    [Fact]
    public async Task Check_now_leaves_exactly_one_readiness_now_command_with_the_owner_actor_and_no_data()
    {
        var before = await ScalarAsync<long>("SELECT count(*) FROM execution.commands WHERE kind = 'readiness_now'");

        using var response = await Client.PostAsJsonAsync(CheckNow, new { }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("commandId").GetGuid();
        (await ScalarAsync<long>("SELECT count(*) FROM execution.commands WHERE kind = 'readiness_now'")).ShouldBe(before + 1);
        (await ScalarAsync<string>("SELECT actor || '|' || payload::text FROM execution.commands WHERE id = @id", ("id", id))).ShouldStartWith("owner:");
        (await ScalarAsync<string>("SELECT payload::text FROM execution.commands WHERE id = @id", ("id", id))).ShouldBe("{}");
    }
}
