using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.LiveTrading;

/// <summary>
/// P11-05 end to end on a real database: the gate Live:Writes:Enabled (checked before the password), a fresh password on every save (a step-up window alone is refused), the actor from the session, the 422 screens and rules, the derived lock end, the audit without the
/// text, the latest per kind with empty, the overdue notice in the bell and its resolution.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ComplianceApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string PrivateNote = "a private note about the call with the provider";

    private static readonly string[] CircularsOnly = ["NSE/INVG/73992", "SEBI/HO/MIRSD/2025/0000013"];

    private static readonly string[] PlainHttp = ["http://example.com/plain"];

    private static readonly Uri Route = new("/api/live-trading/compliance", UriKind.Relative);

    private IConfigurationRoot Configuration => (IConfigurationRoot)Factory.Services.GetRequiredService<IConfiguration>();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Configuration["Live:Writes:Enabled"] = "true";
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "TRUNCATE trading.compliance_records RESTART IDENTITY");
        await ExecuteAsync(connection, "DELETE FROM accounts.account_events WHERE event = 'ComplianceRecorded'");
    }

    public override async ValueTask DisposeAsync()
    {
        Configuration["Live:Writes:Enabled"] = null;
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "TRUNCATE trading.compliance_records RESTART IDENTITY");
        await base.DisposeAsync();
    }

    private static Dictionary<string, object?> Body(string? password, Action<Dictionary<string, object?>>? change = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["kind"] = "sebi_recheck",
            ["outcome"] = "rechecked_no_change",
            ["decidedOn"] = "2026-10-12",
            ["summary"] = PrivateNote,
            ["sources"] = new[] { "https://dhanhq.co/docs/v2/authentication/", "NSE/INVG/73992" },
            ["nextReviewOn"] = "2027-01-10",
            ["password"] = password,
        };
        change?.Invoke(body);
        return body;
    }

    private async Task<(HttpStatusCode Status, JsonElement Json)> PostAsync(object body, HttpClient? client = null)
    {
        using var response = await (client ?? Client).PostAsJsonAsync(Route, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<long> RowsAsync(string where = "true") => ScalarAsync<long>($"SELECT count(*) FROM trading.compliance_records WHERE {where}");

    [Fact]
    public async Task Without_a_session_both_routes_are_401()
    {
        using var read = await Factory.CreateAnonymousClient().GetAsync(Route, Ct);
        using var write = await Factory.CreateAnonymousClient().PostAsJsonAsync(Route, Body(Factory.OwnerPassword), Ct);

        (read.StatusCode, write.StatusCode).ShouldBe((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized));
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_fresh_database_lists_nothing_and_every_kind_is_empty()
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(Route, Ct);

        view.GetProperty("items").GetArrayLength().ShouldBe(0);
        foreach (var kind in new[] { "static_ip", "sebi_recheck", "dhan_ip_read_policy" })
        {
            view.GetProperty("latest").GetProperty(kind).ValueKind.ShouldBe(JsonValueKind.Null);
        }

        view.GetProperty("writesEnabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_closed_gate_is_403_live_capability_off_before_the_password_is_looked_at()
    {
        Configuration["Live:Writes:Enabled"] = null;
        var wrongPassword = await PostAsync(Body("not-the-password"));
        var rightPassword = await PostAsync(Body(Factory.OwnerPassword));
        var view = await Client.GetFromJsonAsync<JsonElement>(Route, Ct);

        wrongPassword.Status.ShouldBe(HttpStatusCode.Forbidden);
        wrongPassword.Json.GetProperty("code").GetString().ShouldBe("live.capability_off");
        rightPassword.Json.GetProperty("code").GetString().ShouldBe("live.capability_off");
        (await RowsAsync()).ShouldBe(0);
        view.GetProperty("writesEnabled").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-password")]
    public async Task A_missing_or_wrong_password_is_401_and_nothing_is_stored(string? password)
    {
        var result = await PostAsync(Body(password));

        result.Status.ShouldBe(HttpStatusCode.Unauthorized);
        result.Json.GetProperty("code").GetString().ShouldBe("auth.invalid_credentials");
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_step_up_window_alone_does_not_count_and_is_not_used_up()
    {
        var elevated = await Factory.SignInElevatedAsync(Ct);

        var without = await PostAsync(Body(null), elevated.Client);
        var with = await PostAsync(Body(Factory.OwnerPassword), elevated.Client);
        using var sensitive = await elevated.Client.GetAsync(new Uri("/api/auth/session", UriKind.Relative), Ct);

        without.Status.ShouldBe(HttpStatusCode.Unauthorized);
        with.Status.ShouldBe(HttpStatusCode.Created);
        sensitive.IsSuccessStatusCode.ShouldBeTrue();
        (await RowsAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task The_right_password_inserts_one_row_for_the_owner_of_the_session_and_one_audit_event_without_the_text()
    {
        var result = await PostAsync(Body(Factory.OwnerPassword, b => { b["actor"] = "owner:evil@example.test"; b["lockEndsOn"] = "2030-01-01"; }));

        result.Status.ShouldBe(HttpStatusCode.Created);
        var id = result.Json.GetProperty("id").GetInt64();
        result.Json.GetProperty("actor").GetString().ShouldBe("owner:" + ApiFactory.OwnerEmail);
        result.Json.GetProperty("overdue").GetBoolean().ShouldBeFalse();
        (await RowsAsync()).ShouldBe(1);
        (await RowsAsync("actor LIKE '%evil%'")).ShouldBe(0, "a body cannot name the actor");
        (await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'ComplianceRecorded'")).ShouldBe($"record={id} kind=sebi_recheck outcome=rechecked_no_change decided=2026-10-12");
        Factory.Logs.All.ShouldContain(l => l.Contains("live.compliance_recorded", StringComparison.Ordinal));
        Factory.Logs.All.ShouldNotContain(l => l.Contains("private note", StringComparison.Ordinal));
        var view = await Client.GetFromJsonAsync<JsonElement>(Route, Ct);
        view.GetProperty("items")[0].GetProperty("summary").GetString().ShouldBe(PrivateNote);
        view.GetProperty("latest").GetProperty("sebi_recheck").GetProperty("id").GetInt64().ShouldBe(id);
        view.GetProperty("latest").GetProperty("static_ip").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_whitelisted_record_gets_its_lock_end_from_the_server_and_a_client_value_is_ignored()
    {
        var result = await PostAsync(Body(Factory.OwnerPassword, b =>
        {
            b["kind"] = "static_ip";
            b["outcome"] = "whitelisted";
            b["staticIpState"] = "whitelisted";
            b["isp"] = "ACT Broadband";
            b["monthlyCost"] = 350.5m;
            b["ipVersion"] = "v4";
            b["whitelistedAt"] = "2026-10-10";
            b["lockEndsOn"] = "2030-01-01";
        }));

        result.Status.ShouldBe(HttpStatusCode.Created);
        result.Json.GetProperty("lockEndsOn").GetString().ShouldBe("2026-10-17");
        (await ScalarAsync<string>("SELECT lock_ends_on::text FROM trading.compliance_records")).ShouldBe("2026-10-17");
        result.Json.GetProperty("monthlyCost").GetDecimal().ShouldBe(350.5m);
    }

    [Theory]
    [InlineData("summary", "the address is 192.168.1.10")]
    [InlineData("summary", "the address is 2001:db8::1")]
    [InlineData("summary", "client 1100000001 asked")]
    [InlineData("summary", "abcdefghijklmnopqrstuvwxyz0123456789ABCD")]
    [InlineData("summary", "   ")]
    [InlineData("kind", "something_else")]
    [InlineData("outcome", "whitelisted")]
    [InlineData("decidedOn", "2026-10-13")]
    [InlineData("nextReviewOn", "2026-10-12")]
    [InlineData("nextReviewOn", "2027-12-12")]
    public async Task A_refused_field_is_422_live_invalid_naming_it_and_nothing_is_stored(string field, string value)
    {
        var result = await PostAsync(Body(Factory.OwnerPassword, b => b[field] = value));

        result.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        result.Json.GetProperty("code").GetString().ShouldBe("live.invalid");
        result.Json.GetProperty("title").GetString().ShouldNotBeNull().ShouldStartWith(field + " ");
        (await RowsAsync()).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'ComplianceRecorded'")).ShouldBe(0);
    }

    [Fact]
    public async Task The_provider_name_is_screened_and_the_sources_are_not_but_have_their_own_pattern()
    {
        var isp = await PostAsync(Body(Factory.OwnerPassword, b => { b["kind"] = "static_ip"; b["outcome"] = "requested"; b["staticIpState"] = "requested"; b["isp"] = "ACT 192.168.1.10"; }));
        var source = await PostAsync(Body(Factory.OwnerPassword, b => b["sources"] = PlainHttp));
        var many = await PostAsync(Body(Factory.OwnerPassword, b => b["sources"] = Enumerable.Repeat("NSE/INVG/73992", 11).ToArray()));
        var circularOnly = await PostAsync(Body(Factory.OwnerPassword, b => b["sources"] = CircularsOnly));

        isp.Json.GetProperty("title").GetString().ShouldNotBeNull().ShouldStartWith("isp ");
        source.Json.GetProperty("title").GetString().ShouldNotBeNull().ShouldStartWith("sources ");
        many.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        circularOnly.Status.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_whitelisted_state_without_its_date_and_the_static_fields_on_another_kind_are_422()
    {
        var noDate = await PostAsync(Body(Factory.OwnerPassword, b => { b["kind"] = "static_ip"; b["outcome"] = "whitelisted"; b["staticIpState"] = "whitelisted"; }));
        var otherKind = await PostAsync(Body(Factory.OwnerPassword, b => b["isp"] = "ACT"));
        var dateOnRequested = await PostAsync(Body(Factory.OwnerPassword, b => { b["kind"] = "static_ip"; b["outcome"] = "requested"; b["staticIpState"] = "requested"; b["whitelistedAt"] = "2026-10-10"; }));

        new[] { noDate.Status, otherKind.Status, dateOnRequested.Status }.ShouldAllBe(s => s == HttpStatusCode.UnprocessableEntity);
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Markup_in_the_text_is_stored_and_returned_as_data()
    {
        var result = await PostAsync(Body(Factory.OwnerPassword, b => b["summary"] = "<script>alert(1)</script> & more"));

        result.Status.ShouldBe(HttpStatusCode.Created);
        result.Json.GetProperty("summary").GetString().ShouldBe("<script>alert(1)</script> & more");
    }

    [Fact]
    public async Task The_list_filters_by_kind_and_limits_and_a_bad_filter_is_422()
    {
        await PostAsync(Body(Factory.OwnerPassword));
        await PostAsync(Body(Factory.OwnerPassword, b => { b["kind"] = "dhan_ip_read_policy"; b["outcome"] = "unanswered"; }));
        await PostAsync(Body(Factory.OwnerPassword, b => b["outcome"] = "rechecked_changed"));

        var all = await Client.GetFromJsonAsync<JsonElement>(Route, Ct);
        var sebi = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/live-trading/compliance?kind=sebi_recheck&limit=1", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri("/api/live-trading/compliance?kind=nope", UriKind.Relative), Ct);
        using var badLimit = await Client.GetAsync(new Uri("/api/live-trading/compliance?limit=0", UriKind.Relative), Ct);

        all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ShouldBe([3, 2, 1]);
        sebi.GetProperty("items").GetArrayLength().ShouldBe(1);
        sebi.GetProperty("latest").GetProperty("dhan_ip_read_policy").GetProperty("outcome").GetString().ShouldBe("unanswered");
        (bad.StatusCode, badLimit.StatusCode).ShouldBe((HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task An_overdue_record_raises_the_bell_notice_and_a_newer_record_of_the_kind_resolves_it()
    {
        // The clock is Monday 2026-10-12; the review date of 2026-10-10 has passed.
        await PostAsync(Body(Factory.OwnerPassword, b => { b["decidedOn"] = "2026-07-12"; b["nextReviewOn"] = "2026-10-10"; }));
        var notices = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/plans/notices", UriKind.Relative), Ct);
        var overdue = await Client.GetFromJsonAsync<JsonElement>(Route, Ct);

        var notice = notices.GetProperty("items").EnumerateArray().Single(n => n.GetProperty("kind").GetString() == "compliance_recheck_overdue");
        notice.GetProperty("route").GetString().ShouldBe("/live?tab=compliance");
        notice.GetProperty("text").GetString().ShouldNotBeNull().ShouldContain("Compliance re-check overdue");
        overdue.GetProperty("latest").GetProperty("sebi_recheck").GetProperty("overdue").GetBoolean().ShouldBeTrue();

        await PostAsync(Body(Factory.OwnerPassword));
        var after = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/plans/notices", UriKind.Relative), Ct);
        after.GetProperty("items").EnumerateArray().ShouldNotContain(n => n.GetProperty("kind").GetString() == "compliance_recheck_overdue");
    }

    [Fact]
    public async Task No_route_of_the_module_is_an_order_route_and_a_put_is_not_a_route()
    {
        using var put = await Client.PutAsJsonAsync(Route, Body(Factory.OwnerPassword), Ct);

        put.StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
        (await RowsAsync()).ShouldBe(0);
    }
}
