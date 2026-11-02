using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.EventFilter;

/// <summary>
/// P15-06 criteria 8 and 9 end to end on a real database: GET and PUT /api/selection/event-filter. A fresh database says OFF, 2 sessions, manual; every change (ON and OFF) needs the session, the CSRF token and a fresh password; the actor is the session's owner and a body cannot name one;
/// the reason and the window are checked (400 selection.reason_invalid, 400 selection.invalid_setting); the row is the newest one and insert-only; one account event of the category selection names the row and never the reason; the auto option is inert (no verdict), and setting approval
/// to auto is an owner act.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class EventFilterApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Reason = "a-reason-that-must-never-reach-the-audit-feed";

    private static readonly Uri Route = new("/api/selection/event-filter", UriKind.Relative);

    private async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(object body)
    {
        using var response = await Client.PutAsJsonAsync(Route, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> SwitchAsync(string? password, bool? enabled = true, int? days = null, string? approval = null, string? reason = Reason) =>
        PutAsync(new { enabled, resultsWithinDays = days, approval, reason, password, actor = "owner:evil@example.test" });

    private Task<long> RowsAsync(string where = "true") => ScalarAsync<long>($"SELECT count(*) FROM filings.event_filter_decisions WHERE {where}");

    [Fact]
    public async Task Without_a_session_both_routes_are_401_and_nothing_is_written()
    {
        using var read = await Factory.CreateAnonymousClient().GetAsync(Route, Ct);
        using var write = await Factory.CreateAnonymousClient().PutAsJsonAsync(Route, new { enabled = true, reason = "x", password = Factory.OwnerPassword }, Ct);

        (read.StatusCode, write.StatusCode).ShouldBe((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized));
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_fresh_database_says_off_two_sessions_manual_with_the_reasons_and_an_empty_history()
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(Route, Ct);

        view.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        view.GetProperty("resultsWithinDays").GetInt32().ShouldBe(2);
        view.GetProperty("approval").GetString().ShouldBe("manual");
        view.GetProperty("decidedBy").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("autoVerdict").GetString().ShouldBe("unavailable");
        view.GetProperty("history").GetArrayLength().ShouldBe(0);
        view.GetProperty("notActingBecause").EnumerateArray().Select(r => r.GetProperty("code").GetString()).ShouldContain("switch_off");
        view.EnumerateObject().Select(p => p.Name).ShouldBe(["enabled", "resultsWithinDays", "approval", "decidedBy", "autoVerdict", "history", "notActingBecause", "instrumentsTracked", "instrumentsWithEntry"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-password")]
    public async Task A_missing_or_wrong_password_is_401_and_nothing_is_written(string? password)
    {
        var change = await SwitchAsync(password);

        change.Status.ShouldBe(HttpStatusCode.Unauthorized);
        change.Body.GetProperty("code").GetString().ShouldBe("auth.invalid_credentials");
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_right_password_and_a_reason_append_an_owner_row_for_the_session_with_the_snapshot_and_one_audit_event_without_the_reason()
    {
        var result = await SwitchAsync(Factory.OwnerPassword, days: 3);

        result.Status.ShouldBe(HttpStatusCode.OK);
        (await RowsAsync()).ShouldBe(1);
        (await RowsAsync("actor LIKE '%evil%'")).ShouldBe(0, "a body cannot name the actor");
        (await ScalarAsync<string>("SELECT actor FROM filings.event_filter_decisions")).ShouldStartWith("owner:");
        (await ScalarAsync<string>("SELECT decided_by FROM filings.event_filter_decisions")).ShouldBe("owner");
        (await ScalarAsync<string>("SELECT setting_snapshot->>'smart_rules.event_filter.results_within_days' FROM filings.event_filter_decisions")).ShouldBe("3");
        (await ScalarAsync<string>("SELECT setting_snapshot->>'smart_rules.event_filter.enabled' FROM filings.event_filter_decisions")).ShouldBe("true");
        (await ScalarAsync<bool>("SELECT verdict_ref IS NULL FROM filings.event_filter_decisions")).ShouldBeTrue();
        (await ScalarAsync<bool>("SELECT changed_at > now() - interval '1 minute' AND changed_at <= now() + interval '1 minute' FROM filings.event_filter_decisions")).ShouldBeTrue("the database stamps the time");
        var detail = await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'EventFilterSwitched'");
        detail.ShouldBe("decision=1 enabled=on results_within_days=3 approval=manual decided_by=owner");
        detail.ShouldNotContain(Reason);
        var view = await Client.GetFromJsonAsync<JsonElement>(Route, Ct);
        view.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        view.GetProperty("resultsWithinDays").GetInt32().ShouldBe(3);
        view.GetProperty("history")[0].GetProperty("reason").GetString().ShouldBe(Reason);
        view.GetProperty("history")[0].GetProperty("actor").GetString().ShouldStartWith("owner:");
    }

    [Fact]
    public async Task Every_change_asks_for_the_password_again_ON_and_OFF_and_the_newest_row_is_the_switch_in_force()
    {
        var on = await SwitchAsync(Factory.OwnerPassword, enabled: true);
        var offWithout = await SwitchAsync(null, enabled: false);
        var off = await SwitchAsync(Factory.OwnerPassword, enabled: false);

        (on.Status, offWithout.Status, off.Status).ShouldBe((HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.OK));
        (await ScalarAsync<string>("SELECT string_agg(enabled::text, ',' ORDER BY id) FROM filings.event_filter_decisions")).ShouldBe("true,false");
        (await Client.GetFromJsonAsync<JsonElement>(Route, Ct)).GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_reason_is_400_selection_reason_invalid_before_the_password_counts(string? reason)
    {
        var result = await SwitchAsync("wrong-password", reason: reason);

        result.Status.ShouldBe(HttpStatusCode.BadRequest);
        result.Body.GetProperty("code").GetString().ShouldBe("selection.reason_invalid");
        (await RowsAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(11, null)]
    [InlineData(-1, null)]
    [InlineData(null, "everything")]
    public async Task A_window_outside_0_to_10_or_an_unknown_approval_is_400_selection_invalid_setting(int? days, string? approval)
    {
        var result = await SwitchAsync(Factory.OwnerPassword, days: days, approval: approval);

        result.Status.ShouldBe(HttpStatusCode.BadRequest);
        result.Body.GetProperty("code").GetString().ShouldBe("selection.invalid_setting");
        (await RowsAsync()).ShouldBe(0);
        (await SwitchAsync(Factory.OwnerPassword, enabled: null)).Body.GetProperty("code").GetString().ShouldBe("selection.invalid_setting");
    }

    [Fact]
    public async Task Setting_the_approval_to_auto_is_an_owner_act_and_with_no_verdict_the_view_says_unavailable_and_nothing_turns_on_by_itself()
    {
        var result = await SwitchAsync(Factory.OwnerPassword, enabled: false, days: 2, approval: "auto");

        result.Status.ShouldBe(HttpStatusCode.OK);
        (await ScalarAsync<string>("SELECT decided_by || '/' || approval || '/' || enabled::text FROM filings.event_filter_decisions")).ShouldBe("owner/auto/false");
        var view = await Client.GetFromJsonAsync<JsonElement>(Route, Ct);
        (view.GetProperty("approval").GetString(), view.GetProperty("autoVerdict").GetString(), view.GetProperty("enabled").GetBoolean()).ShouldBe(("auto", "unavailable", false));
    }

    [Fact]
    public async Task The_history_lists_the_latest_50_rows_newest_first()
    {
        for (var i = 0; i < 3; i++)
        {
            (await SwitchAsync(Factory.OwnerPassword, enabled: i % 2 == 0, days: i)).Status.ShouldBe(HttpStatusCode.OK);
        }

        var history = (await Client.GetFromJsonAsync<JsonElement>(Route, Ct)).GetProperty("history");

        history.EnumerateArray().Select(r => r.GetProperty("resultsWithinDays").GetInt32()).ShouldBe([2, 1, 0]);
    }
}
