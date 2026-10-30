using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>
/// Spec P12-06 criterion 12 on a real database: the reconciliation read, and the owner's resolution of a break behind the gate Live:Writes:Enabled (asked before the password), a fresh password, the actor from the session, and the rules answered as 400, 404 and 409.
/// The record tables are insert-only, so each test uses its own account id. The freeze release then follows the resolutions.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExecutionReconciliationApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private IConfigurationRoot Configuration => (IConfigurationRoot)Factory.Services.GetRequiredService<IConfiguration>();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Configuration["Live:Writes:Enabled"] = "true";
    }

    public override async ValueTask DisposeAsync()
    {
        Configuration["Live:Writes:Enabled"] = null;
        await base.DisposeAsync();
    }

    private static Uri Resolve(long id) => new($"/api/execution/breaks/{id}/resolve", UriKind.Relative);

    private async Task<(long Run, long Break)> BreakAsync(Guid account, string kind = "position_mismatch", bool readable = true)
    {
        await using var connection = await OpenAsync();
        await using var run = new Npgsql.NpgsqlCommand("INSERT INTO execution.recon_runs (at, account_id, session_date, kind, readable, orders, trades, positions, breaks) VALUES (@at, @a, '2026-10-30', 'continuous', @readable, 1, 1, 1, 1) RETURNING id", connection);
        run.Parameters.AddWithValue("at", Now);
        run.Parameters.AddWithValue("a", account);
        run.Parameters.AddWithValue("readable", readable);
        var runId = (long)(await run.ExecuteScalarAsync(Ct))!;
        await using var insert = new Npgsql.NpgsqlCommand("INSERT INTO execution.recon_breaks (run_id, account_id, session_date, kind, severity, symbol, key, ours, theirs, detail, at) VALUES (@r, @a, '2026-10-30', @kind, 'critical', 'TCS', @key, '\"10\"', '\"13\"', 'net differs', @at) RETURNING id", connection);
        insert.Parameters.AddWithValue("r", runId);
        insert.Parameters.AddWithValue("a", account);
        insert.Parameters.AddWithValue("kind", kind);
        insert.Parameters.AddWithValue("key", kind + ":" + Guid.NewGuid().ToString("N"));
        insert.Parameters.AddWithValue("at", Now);
        return (runId, (long)(await insert.ExecuteScalarAsync(Ct))!);
    }

    private async Task<(HttpStatusCode Status, JsonElement Json)> PostAsync(Uri route, object body)
    {
        using var response = await Client.PostAsJsonAsync(route, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<long> ResolutionsAsync(long id) => ScalarAsync<long>("SELECT count(*) FROM execution.break_resolutions WHERE break_id = @b", ("b", id));

    [Fact]
    public async Task Without_a_session_both_routes_are_401()
    {
        var anonymous = Factory.CreateAnonymousClient();

        using var read = await anonymous.GetAsync(new Uri("/api/execution/reconciliation", UriKind.Relative), Ct);
        using var resolve = await anonymous.PostAsJsonAsync(Resolve(1), new { resolution = "accept_broker", password = Factory.OwnerPassword }, Ct);

        (read.StatusCode, resolve.StatusCode).ShouldBe((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task The_reconciliation_read_shows_the_runs_and_the_breaks_with_their_resolution_filtered_by_account_and_date()
    {
        var account = Guid.NewGuid();
        var (_, brk) = await BreakAsync(account);
        await BreakAsync(account, "trade_extra");
        await BreakAsync(Guid.NewGuid());
        var resolved = await PostAsync(Resolve(brk), new { resolution = "accept_broker", note = "checked in the Dhan app", password = Factory.OwnerPassword });
        resolved.Status.ShouldBe(HttpStatusCode.OK);

        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution/reconciliation?accountId={account}&date=2026-10-30", UriKind.Relative), Ct);
        var other = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution/reconciliation?accountId={account}&date=2026-10-31", UriKind.Relative), Ct);

        view.GetProperty("runs").GetArrayLength().ShouldBe(2);
        var breaks = view.GetProperty("breaks").EnumerateArray().ToList();
        breaks.Select(b => b.GetProperty("kind").GetString()).ShouldBe(["trade_extra", "position_mismatch"], "newest first");
        breaks[1].GetProperty("resolution").GetProperty("resolution").GetString().ShouldBe("accept_broker");
        breaks[1].GetProperty("resolution").GetProperty("note").GetString().ShouldBe("checked in the Dhan app");
        breaks[0].GetProperty("resolution").ValueKind.ShouldBe(JsonValueKind.Null);
        other.GetProperty("breaks").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_closed_gate_is_403_before_the_password_is_looked_at_and_writes_nothing()
    {
        var (_, brk) = await BreakAsync(Guid.NewGuid());
        Configuration["Live:Writes:Enabled"] = null;

        var wrong = await PostAsync(Resolve(brk), new { resolution = "accept_broker", password = "not-the-password" });
        var right = await PostAsync(Resolve(brk), new { resolution = "accept_broker", password = Factory.OwnerPassword });

        wrong.Status.ShouldBe(HttpStatusCode.Forbidden);
        wrong.Json.GetProperty("code").GetString().ShouldBe("live.capability_off");
        right.Json.GetProperty("code").GetString().ShouldBe("live.capability_off");
        (await ResolutionsAsync(brk)).ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-password")]
    public async Task A_missing_or_wrong_password_is_401_and_the_break_stays_open(string? password)
    {
        var (_, brk) = await BreakAsync(Guid.NewGuid());

        var result = await PostAsync(Resolve(brk), new { resolution = "accept_broker", password });

        result.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await ResolutionsAsync(brk)).ShouldBe(0);
    }

    [Fact]
    public async Task A_fresh_password_resolves_with_the_actor_from_the_session_once_and_the_row_keeps_the_note()
    {
        var (_, brk) = await BreakAsync(Guid.NewGuid());

        var first = await PostAsync(Resolve(brk), new { resolution = "accept_broker", note = "same as the broker", password = Factory.OwnerPassword, actor = "owner:evil@example.test" });
        var again = await PostAsync(Resolve(brk), new { resolution = "accept_broker", password = Factory.OwnerPassword });

        first.Status.ShouldBe(HttpStatusCode.OK);
        first.Json.GetProperty("resolution").GetProperty("by").GetString().ShouldBe("owner:" + ApiFactory.OwnerEmail);
        (await ScalarAsync<string>("SELECT resolved_by FROM execution.break_resolutions WHERE break_id = @b", ("b", brk))).ShouldBe("owner:" + ApiFactory.OwnerEmail);
        again.Status.ShouldBe(HttpStatusCode.Conflict);
        again.Json.GetProperty("code").GetString().ShouldBe("execution.break_resolved");
    }

    [Fact]
    public async Task An_unknown_break_is_404_a_bad_word_a_dismiss_of_a_non_foreign_item_and_a_long_or_secret_note_are_400_and_nothing_is_written()
    {
        var (_, plain) = await BreakAsync(Guid.NewGuid());
        var (_, foreign) = await BreakAsync(Guid.NewGuid(), "foreign_conflict");

        var unknown = await PostAsync(Resolve(long.MaxValue), new { resolution = "accept_broker", password = Factory.OwnerPassword });
        var word = await PostAsync(Resolve(plain), new { resolution = "make_it_so", password = Factory.OwnerPassword });
        var dismiss = await PostAsync(Resolve(plain), new { resolution = "dismiss_foreign", password = Factory.OwnerPassword });
        var longNote = await PostAsync(Resolve(plain), new { resolution = "accept_broker", note = new string('x', 301), password = Factory.OwnerPassword });
        var secret = await PostAsync(Resolve(plain), new { resolution = "accept_broker", note = "my password is hunter2", password = Factory.OwnerPassword });
        var dismissedForeign = await PostAsync(Resolve(foreign), new { resolution = "dismiss_foreign", password = Factory.OwnerPassword });

        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.Json.GetProperty("code").GetString().ShouldBe("execution.break_unknown");
        new[] { word, dismiss, longNote, secret }.ShouldAllBe(r => r.Status == HttpStatusCode.BadRequest);
        word.Json.GetProperty("code").GetString().ShouldBe("execution.resolution_invalid");
        dismiss.Json.GetProperty("title").GetString()!.ShouldContain("dismiss_foreign_only_for_foreign");
        (await ResolutionsAsync(plain)).ShouldBe(0);
        dismissedForeign.Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_freeze_from_a_break_is_released_through_the_api_only_after_every_break_of_the_account_is_resolved()
    {
        var account = Guid.NewGuid();
        var (_, one) = await BreakAsync(account);
        var (_, two) = await BreakAsync(account, "trade_extra");
        long freeze;
        await using (var connection = await OpenAsync())
        {
            await using var command = new Npgsql.NpgsqlCommand("INSERT INTO execution.halts (at, scope, account_id, level, trigger_code, source, actor, reason) VALUES (@at, 'account', @a, 'freeze', 'reconciliation_break', 'system', 'system:halts', 'break') RETURNING id", connection);
            command.Parameters.AddWithValue("at", Now);
            command.Parameters.AddWithValue("a", account);
            freeze = (long)(await command.ExecuteScalarAsync(Ct))!;
        }

        var release = new Uri($"/api/execution/halts/{freeze}/release", UriKind.Relative);
        var early = await PostAsync(release, new { reason = "looks fine", password = Factory.OwnerPassword });
        (await PostAsync(Resolve(one), new { resolution = "accept_broker", password = Factory.OwnerPassword })).Status.ShouldBe(HttpStatusCode.OK);
        var half = await PostAsync(release, new { reason = "looks fine", password = Factory.OwnerPassword });
        (await PostAsync(Resolve(two), new { resolution = "accept_broker", password = Factory.OwnerPassword })).Status.ShouldBe(HttpStatusCode.OK);
        var done = await PostAsync(release, new { reason = "all breaks resolved", password = Factory.OwnerPassword });

        early.Status.ShouldBe(HttpStatusCode.Conflict);
        early.Json.GetProperty("title").GetString()!.ShouldContain("freeze_needs_reconciliation");
        half.Status.ShouldBe(HttpStatusCode.Conflict);
        done.Status.ShouldBe(HttpStatusCode.OK);
        done.Json.GetProperty("end").GetProperty("how").GetString().ShouldBe("owner");
    }

    [Fact]
    public async Task There_is_no_route_that_edits_deletes_or_creates_a_break_or_a_run()
    {
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            using var request = new HttpRequestMessage(method, new Uri("/api/execution/reconciliation", UriKind.Relative)) { Content = JsonContent.Create(new { }) };
            using var response = await Client.SendAsync(request, Ct);
            ((int)response.StatusCode).ShouldBeOneOf(404, 405);
        }
    }
}
