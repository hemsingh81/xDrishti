using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.OptionBackfill;

/// <summary>
/// P14-03 criteria 6 and 9 end to end on a real database: the owner's backfill switch and the acceptance of the probe result are fresh-password acts on every change (ON and OFF), the actor is the session, a reason is needed, the rows are insert-only, one audit event names
/// the row and never the reason, and the Services screen shows the backfill paused with no "Run now".
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionBackfillApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string SecretReason = "a-reason-that-must-never-reach-the-audit-feed";

    private static readonly Uri Backfill = new("/api/options/backfill", UriKind.Relative);

    private static readonly Uri Acceptance = new("/api/options/backfill/probe-acceptance", UriKind.Relative);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "TRUNCATE options.backfill_switch, options.backfill_probe_ack RESTART IDENTITY");
        await ExecuteAsync(connection, "DELETE FROM accounts.account_events WHERE event IN ('OptionBackfillSwitched', 'OptionBackfillProbeAccepted')");
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(Uri uri, object body)
    {
        using var response = await Client.PutAsJsonAsync(uri, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> SwitchAsync(string? password, bool enabled = true, string? reason = SecretReason) =>
        PutAsync(Backfill, new { enabled, reason, password, actor = "owner:evil@example.test" });

    private Task<long> RowsAsync(string table, string where = "true") => ScalarAsync<long>($"SELECT count(*) FROM options.{table} WHERE {where}");

    [Fact]
    public async Task Without_a_session_all_three_routes_are_401()
    {
        using var read = await Factory.CreateAnonymousClient().GetAsync(Backfill, Ct);
        using var write = await Factory.CreateAnonymousClient().PutAsJsonAsync(Backfill, new { enabled = true, reason = "x", password = Factory.OwnerPassword }, Ct);
        using var accept = await Factory.CreateAnonymousClient().PutAsJsonAsync(Acceptance, new { note = "x", password = Factory.OwnerPassword }, Ct);

        (read.StatusCode, write.StatusCode, accept.StatusCode).ShouldBe((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized));
        (await RowsAsync("backfill_switch")).ShouldBe(0);
        (await RowsAsync("backfill_probe_ack")).ShouldBe(0);
    }

    [Fact]
    public async Task A_fresh_database_says_off_not_accepted_with_the_reasons_and_an_empty_history_and_no_bars()
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(Backfill, Ct);

        view.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        view.GetProperty("switchId").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("probeAccepted").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("history").GetArrayLength().ShouldBe(0);
        view.GetProperty("notRunningBecause").GetArrayLength().ShouldBeGreaterThanOrEqualTo(3);
        view.GetProperty("notice").GetString().ShouldNotBeNull().ShouldContain("nothing reads them");
        view.EnumerateObject().Select(p => p.Name).ShouldBe(["enabled", "switchId", "probeAccepted", "state", "lastSeenAt", "progress", "notRunningBecause", "notice", "history"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-password")]
    public async Task A_missing_or_wrong_password_is_401_and_nothing_is_written(string? password)
    {
        var change = await SwitchAsync(password);
        var accept = await PutAsync(Acceptance, new { note = "x", password });

        change.Status.ShouldBe(HttpStatusCode.Unauthorized);
        accept.Status.ShouldBe(HttpStatusCode.Unauthorized);
        change.Body.GetProperty("code").GetString().ShouldBe("auth.invalid_credentials");
        (await RowsAsync("backfill_switch")).ShouldBe(0);
        (await RowsAsync("backfill_probe_ack")).ShouldBe(0);
    }

    [Fact]
    public async Task The_right_password_and_a_reason_append_a_row_for_the_owner_of_the_session_and_one_audit_event_without_the_reason()
    {
        var result = await SwitchAsync(Factory.OwnerPassword);

        result.Status.ShouldBe(HttpStatusCode.OK);
        var id = result.Body.GetProperty("id").GetInt64();
        (await RowsAsync("backfill_switch")).ShouldBe(1);
        (await RowsAsync("backfill_switch", "actor LIKE '%evil%'")).ShouldBe(0, "a body cannot name the actor");
        (await ScalarAsync<string>("SELECT actor FROM options.backfill_switch")).ShouldStartWith("owner:");
        var detail = await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'OptionBackfillSwitched'");
        detail.ShouldBe($"switch={id} option_backfill=on");
        detail.ShouldNotContain(SecretReason);
        var view = await Client.GetFromJsonAsync<JsonElement>(Backfill, Ct);
        view.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        view.GetProperty("history")[0].GetProperty("reason").GetString().ShouldBe(SecretReason);
    }

    [Fact]
    public async Task Every_change_asks_for_the_password_again_ON_and_OFF()
    {
        var on = await SwitchAsync(Factory.OwnerPassword, enabled: true);
        var offWithout = await SwitchAsync(null, enabled: false);
        var off = await SwitchAsync(Factory.OwnerPassword, enabled: false);

        (on.Status, offWithout.Status, off.Status).ShouldBe((HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.OK));
        (await ScalarAsync<string>("SELECT string_agg(enabled::text, ',' ORDER BY id) FROM options.backfill_switch")).ShouldBe("true,false");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_missing_reason_or_note_with_the_right_password_is_400_and_writes_nothing(string? text)
    {
        var change = await SwitchAsync(Factory.OwnerPassword, reason: text);
        var accept = await PutAsync(Acceptance, new { note = text, password = Factory.OwnerPassword });

        (change.Status, accept.Status).ShouldBe((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest));
        change.Body.GetProperty("code").GetString().ShouldBe("options.reason_invalid");
        (await RowsAsync("backfill_switch")).ShouldBe(0);
        (await RowsAsync("backfill_probe_ack")).ShouldBe(0);
    }

    [Fact]
    public async Task The_owner_accepts_the_probe_result_with_a_fresh_password_and_the_view_and_the_audit_show_it()
    {
        var result = await PutAsync(Acceptance, new { note = "table of ADR 0177 filled", password = Factory.OwnerPassword, actor = "owner:evil@example.test" });

        result.Status.ShouldBe(HttpStatusCode.OK);
        var id = result.Body.GetProperty("id").GetInt64();
        (await RowsAsync("backfill_probe_ack", "actor LIKE '%evil%'")).ShouldBe(0);
        (await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'OptionBackfillProbeAccepted'")).ShouldBe($"probe_ack={id}");
        var view = await Client.GetFromJsonAsync<JsonElement>(Backfill, Ct);
        view.GetProperty("probeAccepted").GetProperty("note").GetString().ShouldBe("table of ADR 0177 filled");
        view.GetProperty("enabled").GetBoolean().ShouldBeFalse("accepting the probe switches nothing on");
    }

    [Fact]
    public async Task A_post_is_not_a_route()
    {
        using var response = await Client.PostAsJsonAsync(Backfill, new { enabled = true, reason = "x", password = Factory.OwnerPassword }, Ct);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
        (await RowsAsync("backfill_switch")).ShouldBe(0);
    }

    [Fact]
    public async Task The_service_is_listed_paused_by_default_and_run_now_is_refused()
    {
        var service = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/services/options-backfill", UriKind.Relative), Ct);
        using var run = await Client.PostAsync(new Uri("/api/services/options-backfill/run", UriKind.Relative), null, Ct);

        service.GetProperty("id").GetString().ShouldBe("options-backfill");
        service.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        run.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await run.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("services.run_now_unavailable");
        (await ScalarAsync<long>("SELECT count(*) FROM platform.service_runs WHERE service_id = 'options-backfill'")).ShouldBe(0);
    }
}
