using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using XDrishti.Api.Cli;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.ChargeSheet;

/// <summary>
/// Spec P8-23 (ADR 0127) end to end on a real database: the owner's act through the HTTP endpoints (a fresh password every time, the capability off by default, the actor from the session), the append-only log, the confirmation going stale when the stored sheet's rates
/// change, a revoke, two confirmations at once, the confirmation that the reports and the paper comparison read, the audit and the verb.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ChargeSheetApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Status = new("/api/charge-sheet/confirmation", UriKind.Relative);

    private static readonly Uri Revoke = new("/api/charge-sheet/confirmation/revoke", UriKind.Relative);

    private static readonly Uri Capability = new("/api/charge-sheet/capability", UriKind.Relative);

    private const string OwnerActor = "owner:" + ApiFactory.OwnerEmail;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanSheetsAsync();
    }

    private async Task CleanSheetsAsync()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM platform.app_settings WHERE key LIKE 'research.cost_sheet.%'");
    }

    private async Task StoreSheetAsync(CostSheet sheet)
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(sheet, "test", DateTimeOffset.UnixEpoch.AddDays(20_000), Ct);
        (await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
    }

    private static CostSheet OtherRates() =>
        CostSheet.Create(FixtureSheet.Id, "FIXTURE sheet (invented numbers)", "invented for tests", null, [TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.IntradayA, TestCostSheet.DeliveryA)]).Value;

    private Task<HttpResponseMessage> ConfirmAsync(string? password, string? rates = null, string sheet = FixtureSheet.Id, string reason = "checked against my contract notes", HttpClient? client = null) =>
        (client ?? Client).PostAsJsonAsync(Status, new { sheetId = sheet, ratesSha256 = rates ?? FixtureSheet.Sheet.RatesSha256, reason, password, actor = "owner:someone-else", confirmed = true }, Ct);

    private Task<HttpResponseMessage> SwitchAsync(bool on, string? password) => Client.PutAsJsonAsync(Capability, new { on, reason = on ? "I want to confirm my sheet" : "done", password }, Ct);

    private Task<HttpResponseMessage> RevokeAsync(string? password, string reason = "wrong sheet") => Client.PostAsJsonAsync(Revoke, new { reason, password }, Ct);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    private async Task<JsonElement> GetStatusAsync(string query = "")
    {
        using var response = await Client.GetAsync(new Uri(Status + query, UriKind.Relative), Ct);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        return await ReadAsync(response);
    }

    private Task<long> RowsAsync() => ScalarAsync<long>("SELECT count(*) FROM workflow.charge_sheet_events");

    private async Task<TResult> WithAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<ChargeSheetConfirmation?> StoredAsync() => WithAsync(p => p.GetRequiredService<IChargeSheetConfirmationSource>().FindAsync(Ct).AsTask());

    private Task<DateOnly?> FactAsync(string sheet) => WithAsync(p => p.GetRequiredService<ICostConfirmationFact>().ConfirmedOnAsync(sheet, Ct).AsTask());

    private async Task SwitchOnAsync() => (await ReadAsync(await SwitchAsync(true, Factory.OwnerPassword))).GetProperty("changed").GetBoolean().ShouldBeTrue();

    [Fact]
    public async Task On_a_fresh_database_nothing_is_confirmed_and_confirming_is_off()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);

        var status = await GetStatusAsync();

        status.GetProperty("state").GetString().ShouldBe("not_confirmed");
        status.GetProperty("capabilityOn").GetBoolean().ShouldBeFalse();
        status.GetProperty("inForce").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("meaning").GetString()!.ShouldContain("places no order");
        (await StoredAsync()).ShouldBeNull();
        (await FactAsync(FixtureSheet.Id)).ShouldBeNull();
    }

    [Fact]
    public void Production_reads_the_stored_owner_confirmation_for_the_paper_comparison()
    {
        using var scope = Factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICostConfirmationFact>().GetType().Name.ShouldBe("StoredCostConfirmation");
    }

    [Theory]
    [InlineData("GET", "/api/charge-sheet/confirmation")]
    [InlineData("POST", "/api/charge-sheet/confirmation")]
    [InlineData("POST", "/api/charge-sheet/confirmation/revoke")]
    [InlineData("PUT", "/api/charge-sheet/capability")]
    public async Task Without_a_session_nothing_can_be_read_or_done(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(new { sheetId = "fixture-a", ratesSha256 = FixtureSheet.Sheet.RatesSha256, reason = "x", password = "x", on = true }) };
        using var response = await Factory.CreateAnonymousClient().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task While_confirming_is_switched_off_the_endpoint_refuses_even_with_the_right_password_and_nothing_is_written()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);

        using var response = await ConfirmAsync(Factory.OwnerPassword);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        body.GetProperty("code").GetString().ShouldBe("chargesheet.capability_off");
        (await RowsAsync()).ShouldBe(0);
        (await StoredAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task A_wrong_or_missing_password_is_refused_for_every_act_and_nothing_is_written()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);
        await SwitchOnAsync();
        var before = await RowsAsync();

        try
        {
            // Three refusals at a time stay under the five-failure lock; a right password in between starts the count again.
            foreach (var password in new string?[] { null, "not-the-password" })
            {
                using var confirm = await ConfirmAsync(password);
                using var revoke = await RevokeAsync(password);
                using var gate = await SwitchAsync(false, password);

                confirm.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
                revoke.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
                gate.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
                using var right = await SwitchAsync(true, Factory.OwnerPassword);
                right.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            (await RowsAsync()).ShouldBe(before);
            (await StoredAsync()).ShouldBeNull();
        }
        finally
        {
            await Factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task An_open_step_up_window_does_not_stand_in_for_the_password_and_is_not_used_up_by_the_act()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);
        var elevated = await Factory.SignInElevatedAsync(Ct);
        using var none = await elevated.Client.PutAsJsonAsync(Capability, new { on = true, reason = "go", password = (string?)null }, Ct);
        using var ok = await elevated.Client.PutAsJsonAsync(Capability, new { on = true, reason = "go", password = Factory.OwnerPassword }, Ct);
        var until = await ScalarAsync<DateTime>("SELECT elevated_until FROM auth.sessions WHERE elevated_until IS NOT NULL ORDER BY elevated_until DESC LIMIT 1");

        using var again = await elevated.Client.PostAsJsonAsync(Revoke, new { reason = "go", password = (string?)null }, Ct);
        using var probe = await elevated.Client.PostAsJsonAsync(new Uri("/api/risk-profiles", UriKind.Relative), new { }, Ct);

        none.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        probe.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden); // the window is still open for the other sensitive actions
        (await ScalarAsync<DateTime>("SELECT elevated_until FROM auth.sessions WHERE elevated_until IS NOT NULL ORDER BY elevated_until DESC LIMIT 1")).ShouldBe(until);
    }

    [Fact]
    public async Task The_owner_switches_it_on_confirms_one_sheet_and_the_reports_and_the_paper_comparison_read_that_confirmation()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);
        await SwitchOnAsync();

        var confirmed = await ReadAsync(await ConfirmAsync(Factory.OwnerPassword));

        confirmed.GetProperty("changed").GetBoolean().ShouldBeTrue();
        var status = confirmed.GetProperty("status");
        status.GetProperty("state").GetString().ShouldBe("confirmed");
        status.GetProperty("capabilityOn").GetBoolean().ShouldBeTrue();
        var inForce = status.GetProperty("inForce");
        inForce.GetProperty("sheetId").GetString().ShouldBe(FixtureSheet.Id);
        inForce.GetProperty("ratesSha256").GetString().ShouldBe(FixtureSheet.Sheet.RatesSha256);
        inForce.GetProperty("actor").GetString().ShouldBe(OwnerActor); // the session's owner, not the body's
        var stored = await StoredAsync();
        stored.ShouldNotBeNull();
        stored.SheetId.ShouldBe(FixtureSheet.Id);
        stored.RatesSha256.ShouldBe(FixtureSheet.Sheet.RatesSha256);
        stored.ConfirmedOn.ShouldBe(await ScalarAsync<DateOnly>("SELECT confirmed_on FROM workflow.charge_sheet_events WHERE kind = 'confirm'"));
        (await FactAsync(FixtureSheet.Id)).ShouldBe(stored.ConfirmedOn);
        (await FactAsync("another-sheet")).ShouldBeNull(); // the confirmation is of ONE sheet
        (await ScalarAsync<string>("SELECT actor FROM workflow.charge_sheet_events WHERE kind = 'confirm'")).ShouldBe(OwnerActor);
    }

    [Fact]
    public async Task A_change_of_the_stored_sheets_rates_makes_the_confirmation_stale_never_silently_still_valid_and_a_new_confirmation_covers_the_new_rates()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);
        await SwitchOnAsync();
        (await ReadAsync(await ConfirmAsync(Factory.OwnerPassword))).GetProperty("status").GetProperty("state").GetString().ShouldBe("confirmed");

        await StoreSheetAsync(OtherRates());

        var stale = await GetStatusAsync();
        stale.GetProperty("state").GetString().ShouldBe("stale");
        stale.GetProperty("text").GetString().ShouldStartWith("Stale");
        stale.GetProperty("sheet").GetProperty("ratesSha256").GetString().ShouldBe(OtherRates().RatesSha256);
        (await StoredAsync()).ShouldBeNull();
        (await FactAsync(FixtureSheet.Id)).ShouldBeNull();

        // Showing the old hash again is not enough: the owner must confirm the rates the sheet has now.
        using var old = await ConfirmAsync(Factory.OwnerPassword, FixtureSheet.Sheet.RatesSha256);
        old.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await StoredAsync()).ShouldBeNull();

        var again = await ReadAsync(await ConfirmAsync(Factory.OwnerPassword, OtherRates().RatesSha256));
        again.GetProperty("status").GetProperty("state").GetString().ShouldBe("confirmed");
        (await StoredAsync())!.RatesSha256.ShouldBe(OtherRates().RatesSha256);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.charge_sheet_events WHERE kind = 'confirm'")).ShouldBe(2); // the first row is still there, unedited
    }

    [Fact]
    public async Task A_revoke_is_another_row_it_ends_the_confirmation_and_works_also_with_confirming_switched_off()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);
        await SwitchOnAsync();
        await ReadAsync(await ConfirmAsync(Factory.OwnerPassword));
        await ReadAsync(await SwitchAsync(false, Factory.OwnerPassword));

        var revoked = await ReadAsync(await RevokeAsync(Factory.OwnerPassword));

        revoked.GetProperty("changed").GetBoolean().ShouldBeTrue();
        revoked.GetProperty("status").GetProperty("state").GetString().ShouldBe("revoked");
        (await StoredAsync()).ShouldBeNull();
        (await FactAsync(FixtureSheet.Id)).ShouldBeNull();
        var kinds = await WithAsync(async _ =>
        {
            var status = await GetStatusAsync();
            return status.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("kind").GetString()).ToList();
        });
        kinds.ShouldBe(["revoke", "gate_off", "confirm", "gate_on"]);
        (await ReadAsync(await RevokeAsync(Factory.OwnerPassword))).GetProperty("changed").GetBoolean().ShouldBeFalse(); // nothing left to revoke
        (await RowsAsync()).ShouldBe(4);
    }

    [Fact]
    public async Task Confirming_again_after_a_revoke_needs_the_capability_to_be_on_and_a_new_act()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);
        await SwitchOnAsync();
        await ReadAsync(await ConfirmAsync(Factory.OwnerPassword));
        await ReadAsync(await RevokeAsync(Factory.OwnerPassword));

        var again = await ReadAsync(await ConfirmAsync(Factory.OwnerPassword));

        again.GetProperty("changed").GetBoolean().ShouldBeTrue();
        (await StoredAsync()).ShouldNotBeNull();
    }

    [Fact]
    public async Task Rates_that_are_not_the_stored_ones_an_unknown_sheet_a_bad_id_and_a_blank_reason_are_refused_and_write_nothing()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);
        await SwitchOnAsync();
        var before = await RowsAsync();

        using var changed = await ConfirmAsync(Factory.OwnerPassword, new string('f', 64));
        using var unknown = await ConfirmAsync(Factory.OwnerPassword, sheet: "no-such-sheet");
        using var badId = await ConfirmAsync(Factory.OwnerPassword, sheet: "Bad Id");
        using var testSheet = await ConfirmAsync(Factory.OwnerPassword, sheet: "test-sheet");
        using var badHash = await ConfirmAsync(Factory.OwnerPassword, "nope");
        using var noReason = await ConfirmAsync(Factory.OwnerPassword, reason: " ");

        changed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await changed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("chargesheet.rates_changed");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        badId.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        testSheet.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        badHash.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RowsAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task Two_confirmations_at_once_make_one_row_and_both_answer_that_the_sheet_is_confirmed()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);
        await SwitchOnAsync();

        var answers = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ => await ReadAsync(await ConfirmAsync(Factory.OwnerPassword))));

        answers.Count(a => a.GetProperty("changed").GetBoolean()).ShouldBe(1);
        answers.ShouldAllBe(a => a.GetProperty("status").GetProperty("state").GetString() == "confirmed");
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.charge_sheet_events WHERE kind = 'confirm'")).ShouldBe(1);
    }

    [Fact]
    public async Task The_status_previews_the_named_sheet_with_the_hash_a_confirmation_must_carry_and_says_when_none_is_stored()
    {
        await StoreSheetAsync(FixtureSheet.Build(FixtureSheet.Id, "my contract notes of October"));

        var shown = await GetStatusAsync("?sheetId=" + FixtureSheet.Id);
        var missing = await GetStatusAsync("?sheetId=nothing-here");
        using var invalid = await Client.GetAsync(new Uri(Status + "?sheetId=Bad%20Id", UriKind.Relative), Ct);

        shown.GetProperty("sheet").GetProperty("checked").GetString().ShouldBe("my contract notes of October");
        shown.GetProperty("sheet").GetProperty("schedules").GetInt32().ShouldBe(2);
        shown.GetProperty("sheet").GetProperty("ratesSha256").GetString().ShouldBe(FixtureSheet.Sheet.RatesSha256); // the label and the note are not rates
        missing.GetProperty("sheet").ValueKind.ShouldBe(JsonValueKind.Null);
        missing.GetProperty("sheetNote").GetString().ShouldBe("No stored charge sheet is named 'nothing-here'.");
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_database_stamps_the_row_and_refuses_a_confirmation_while_off_whatever_the_application_says()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);
        await using var connection = await OpenAsync();
        const string Confirm = "INSERT INTO workflow.charge_sheet_events (kind, sheet_id, rates_sha256, confirmed_on, actor, reason) VALUES ('confirm', 'fixture-a', @rates, '2000-01-01', 'owner:x@y.z', 'direct')";

        var refused = await Should.ThrowAsync<Npgsql.PostgresException>(() => ExecuteAsync(connection, Confirm, ("rates", FixtureSheet.Sheet.RatesSha256)));
        await ExecuteAsync(connection, "INSERT INTO workflow.charge_sheet_events (kind, actor, reason) VALUES ('gate_on', 'owner:x@y.z', 'direct')");
        await ExecuteAsync(connection, Confirm, ("rates", FixtureSheet.Sheet.RatesSha256));

        refused.SqlState.ShouldBe(Npgsql.PostgresErrorCodes.CheckViolation);
        (await ScalarAsync<bool>("SELECT confirmed_on = (created_at AT TIME ZONE 'Asia/Kolkata')::date FROM workflow.charge_sheet_events WHERE kind = 'confirm'")).ShouldBeTrue();
        (await StoredAsync())!.SheetId.ShouldBe(FixtureSheet.Id);
    }

    [Fact]
    public async Task A_hand_written_settings_row_confirms_nothing()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, $"INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('reports.charge_sheet_confirmation', '{{\"sheet_id\":\"fixture-a\",\"rates_sha256\":\"{FixtureSheet.Sheet.RatesSha256}\",\"confirmed_on\":\"2026-10-01\"}}', 'test', now())");

        (await StoredAsync()).ShouldBeNull();
        (await FactAsync(FixtureSheet.Id)).ShouldBeNull();
        (await GetStatusAsync()).GetProperty("state").GetString().ShouldBe("not_confirmed");
        await ExecuteAsync(connection, "DELETE FROM platform.app_settings WHERE key = 'reports.charge_sheet_confirmation'");
    }

    [Fact]
    public async Task The_audit_shows_each_act_with_the_owner_and_the_reason_and_no_password()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);
        await SwitchOnAsync();
        await ReadAsync(await ConfirmAsync(Factory.OwnerPassword));
        await ReadAsync(await RevokeAsync(Factory.OwnerPassword));

        using var response = await Client.GetAsync(new Uri("/api/audit/entries?from=2026-10-01&to=2027-09-01&source=chargesheet&limit=50", UriKind.Relative), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var items = JsonDocument.Parse(text).RootElement.GetProperty("items").EnumerateArray().ToList();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        items.Select(i => i.GetProperty("kind").GetString()).ShouldBe(["chargesheet.revoke", "chargesheet.confirm", "chargesheet.gate_on"]);
        items.ShouldAllBe(i => i.GetProperty("actorKind").GetString() == "owner");
        items.ShouldAllBe(i => i.GetProperty("outcome").GetString() == "ok");
        text.ShouldContain("checked against my contract notes");
        text.ShouldNotContain(Factory.OwnerPassword);
    }

    private async Task<(int Exit, string Out, string Error)> VerbAsync(params string[] args)
    {
        var overrides = new Dictionary<string, string?> { ["Database:ConnectionString"] = Factory.AdminConnectionString, ["BrokerVault:Key"] = string.Empty };
        using var host = SimulateHost.Create(overrides, services => services.AddSingleton<ILogEventSink>(new CapturedLogs()), "xd-charge-sheet");
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await ChargeSheetCommand.RunAsync(args, host.Services, new SimulateConsole(output, error), Ct);
        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task The_verb_shows_what_stands_and_refuses_to_confirm_or_revoke_because_that_needs_the_owners_password()
    {
        await StoreSheetAsync(FixtureSheet.Sheet);
        await SwitchOnAsync();
        await ReadAsync(await ConfirmAsync(Factory.OwnerPassword, reason: "checked\u001b[31m red"));
        var before = await RowsAsync();

        var show = await VerbAsync("show", "--sheet", FixtureSheet.Id);
        var confirm = await VerbAsync("confirm", "--sheet", FixtureSheet.Id);
        var revoke = await VerbAsync("revoke");
        var usage = await VerbAsync("approve");

        show.Exit.ShouldBe(0);
        show.Out.ShouldContain("charge sheet confirmation: confirmed");
        show.Out.ShouldContain(FixtureSheet.Sheet.RatesSha256);
        show.Out.ShouldContain("confirming is switched on");
        show.Out.ShouldNotContain("\u001b"); // typed text cannot write escape sequences to the terminal
        show.Out.ShouldContain("checked?[31m red");
        confirm.Exit.ShouldBe(1);
        confirm.Error.ShouldContain("needs the owner's password");
        revoke.Exit.ShouldBe(1);
        usage.Exit.ShouldBe(2);
        (await RowsAsync()).ShouldBe(before);
        (await StoredAsync()).ShouldNotBeNull();
    }
}
