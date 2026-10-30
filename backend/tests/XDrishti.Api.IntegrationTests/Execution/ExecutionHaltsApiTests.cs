using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>
/// Spec P12-05 criteria 6 and 7 on a real database: the halts are read only and newest first; the owner's button needs a session and no password (exit_all needs the step-up window) and only leaves a command row; the release is the owner's alone, behind the
/// gate Live:Writes:Enabled (asked before the password), a fresh password, the actor from the session, and every rule answered together as 409. The halt tables are insert-only, so each test uses its own account id.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExecutionHaltsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Halts = new("/api/execution/halts", UriKind.Relative);

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

    private async Task<long> HaltAsync(Guid account, string level, string trigger, string source = "owner", int secondsAgo = 0)
    {
        await using var connection = await OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("INSERT INTO execution.halts (at, scope, account_id, level, trigger_code, source, actor, reason) VALUES (@at, 'account', @a, @level, @code, @source, @actor, 'because') RETURNING id", connection);
        command.Parameters.AddWithValue("at", Now.AddSeconds(-secondsAgo));
        command.Parameters.AddWithValue("a", account);
        command.Parameters.AddWithValue("level", level);
        command.Parameters.AddWithValue("code", trigger);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("actor", source == "owner" ? "owner:hem@example.invalid" : "system:halts");
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<(HttpStatusCode Status, JsonElement Json)> PostAsync(Uri route, object body, HttpClient? client = null)
    {
        using var response = await (client ?? Client).PostAsJsonAsync(route, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Uri Release(long id) => new($"/api/execution/halts/{id}/release", UriKind.Relative);

    private Task<long> CommandsAsync(Guid account) => ScalarAsync<long>("SELECT count(*) FROM execution.commands WHERE kind = 'halt' AND account_id = @a", ("a", account));

    [Fact]
    public async Task Without_a_session_every_halt_route_is_401()
    {
        var anonymous = Factory.CreateAnonymousClient();

        using var read = await anonymous.GetAsync(Halts, Ct);
        using var start = await anonymous.PostAsJsonAsync(Halts, new { scope = "system", level = "cancel", reason = "x" }, Ct);
        using var release = await anonymous.PostAsJsonAsync(Release(1), new { reason = "x", password = Factory.OwnerPassword }, Ct);

        (read.StatusCode, start.StatusCode, release.StatusCode).ShouldBe((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task The_halts_come_newest_first_with_their_end_and_the_active_filter_keeps_only_those_in_force()
    {
        var account = Guid.NewGuid();
        var oldest = await HaltAsync(account, "entries", "feed_loss", "system", 30);
        await HaltAsync(account, "cancel", "daily_loss", "owner", 20);
        await HaltAsync(account, "exit_all", "owner_button", "owner", 10);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO execution.halt_auto_clears (halt_id, at, trigger_code, detail) VALUES (@id, now(), 'feed_loss', '60 s quiet')", ("id", oldest));
        }

        var all = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution/halts?accountId={account}", UriKind.Relative), Ct);
        var active = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution/halts?accountId={account}&active=true", UriKind.Relative), Ct);

        all.EnumerateArray().Select(h => h.GetProperty("trigger").GetString()).ShouldBe(["owner_button", "daily_loss", "feed_loss"]);
        var ended = all.EnumerateArray().Last();
        ended.GetProperty("end").GetProperty("how").GetString().ShouldBe("auto");
        ended.GetProperty("end").GetProperty("by").GetString().ShouldBe("system:halts");
        all.EnumerateArray().First().GetProperty("end").ValueKind.ShouldBe(JsonValueKind.Null);
        all.EnumerateArray().First().GetProperty("level").GetString().ShouldBe("exit_all");
        active.EnumerateArray().Select(h => h.GetProperty("trigger").GetString()).ShouldBe(["owner_button", "daily_loss"]);
    }

    [Fact]
    public async Task Take_outside_1_to_200_is_a_400()
    {
        foreach (var take in new[] { 0, 201 })
        {
            using var response = await Client.GetAsync(new Uri($"/api/execution/halts?take={take}", UriKind.Relative), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Starting_a_cancel_halt_needs_no_password_and_leaves_exactly_one_command_row_with_the_owner_actor_from_the_session()
    {
        var account = Guid.NewGuid();

        var result = await PostAsync(Halts, new { scope = "account", accountId = account, level = "cancel", reason = "news", actor = "owner:evil@example.test" });

        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Json.GetProperty("commandId").GetGuid().ShouldNotBe(Guid.Empty);
        (await CommandsAsync(account)).ShouldBe(1);
        (await ScalarAsync<string>("SELECT actor FROM execution.commands WHERE kind = 'halt' AND account_id = @a", ("a", account))).ShouldBe("owner:" + ApiFactory.OwnerEmail);
        (await ScalarAsync<string>("SELECT payload::text FROM execution.commands WHERE kind = 'halt' AND account_id = @a", ("a", account))).ShouldContain("\"level\": \"cancel\"");
    }

    [Fact]
    public async Task Exit_all_needs_the_step_up_window_and_with_it_goes_through()
    {
        var account = Guid.NewGuid();
        var body = new { scope = "account", accountId = account, level = "exit_all", reason = "gap down" };

        var without = await PostAsync(Halts, body);
        var elevated = await Factory.SignInElevatedAsync(Ct);
        var with = await PostAsync(Halts, body, elevated.Client);

        without.Status.ShouldBe(HttpStatusCode.Forbidden);
        without.Json.GetProperty("code").GetString().ShouldBe("auth.step_up_required");
        with.Status.ShouldBe(HttpStatusCode.OK);
        (await CommandsAsync(account)).ShouldBe(1);
    }

    [Theory]
    [InlineData("freeze", "gap")]
    [InlineData("none", "gap")]
    [InlineData("cancel", "")]
    [InlineData("cancel", "here is my password")]
    public async Task A_bad_halt_is_a_400_and_leaves_nothing(string level, string reason)
    {
        var account = Guid.NewGuid();

        var result = await PostAsync(Halts, new { scope = "account", accountId = account, level, reason });

        result.Status.ShouldBe(HttpStatusCode.BadRequest);
        result.Json.GetProperty("code").GetString().ShouldBe("execution.halt_invalid");
        (await CommandsAsync(account)).ShouldBe(0);
    }

    [Fact]
    public async Task A_closed_gate_is_403_live_capability_off_before_the_password_is_looked_at_and_writes_nothing()
    {
        var halt = await HaltAsync(Guid.NewGuid(), "entries", "daily_loss");
        Configuration["Live:Writes:Enabled"] = null;

        var wrong = await PostAsync(Release(halt), new { reason = "reviewed", password = "not-the-password" });
        var right = await PostAsync(Release(halt), new { reason = "reviewed", password = Factory.OwnerPassword });

        wrong.Status.ShouldBe(HttpStatusCode.Forbidden);
        wrong.Json.GetProperty("code").GetString().ShouldBe("live.capability_off");
        right.Json.GetProperty("code").GetString().ShouldBe("live.capability_off");
        (await ScalarAsync<long>("SELECT count(*) FROM execution.halt_releases WHERE halt_id = @h", ("h", halt))).ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-password")]
    public async Task A_missing_or_wrong_password_is_401_and_the_halt_stays(string? password)
    {
        var halt = await HaltAsync(Guid.NewGuid(), "entries", "daily_loss");

        var result = await PostAsync(Release(halt), new { reason = "reviewed", password });

        result.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await ScalarAsync<long>("SELECT count(*) FROM execution.halt_releases WHERE halt_id = @h", ("h", halt))).ShouldBe(0);
    }

    [Fact]
    public async Task A_step_up_window_alone_does_not_release_and_a_fresh_password_does_with_the_actor_from_the_session_and_only_once()
    {
        var halt = await HaltAsync(Guid.NewGuid(), "entries", "daily_loss");
        var elevated = await Factory.SignInElevatedAsync(Ct);

        var windowOnly = await PostAsync(Release(halt), new { reason = "reviewed", actor = "owner:evil@example.test" }, elevated.Client);
        var released = await PostAsync(Release(halt), new { reason = "reviewed the loss", password = Factory.OwnerPassword, actor = "owner:evil@example.test" });
        var again = await PostAsync(Release(halt), new { reason = "once more", password = Factory.OwnerPassword });

        windowOnly.Status.ShouldBe(HttpStatusCode.Unauthorized);
        released.Status.ShouldBe(HttpStatusCode.OK);
        released.Json.GetProperty("end").GetProperty("how").GetString().ShouldBe("owner");
        released.Json.GetProperty("end").GetProperty("by").GetString().ShouldBe("owner:" + ApiFactory.OwnerEmail);
        (await ScalarAsync<string>("SELECT released_by FROM execution.halt_releases WHERE halt_id = @h", ("h", halt))).ShouldBe("owner:" + ApiFactory.OwnerEmail);
        again.Status.ShouldBe(HttpStatusCode.Conflict);
        again.Json.GetProperty("code").GetString().ShouldBe("execution.release_refused");
        again.Json.GetProperty("title").GetString()!.ShouldContain("already_ended");
    }

    [Fact]
    public async Task A_freeze_and_a_short_drawdown_review_are_409_with_every_reason_and_an_unknown_halt_is_404()
    {
        var freeze = await HaltAsync(Guid.NewGuid(), "freeze", "reconciliation_break", "system");
        var drawdown = await HaltAsync(Guid.NewGuid(), "entries", "drawdown", "system");

        var frozen = await PostAsync(Release(freeze), new { reason = "all good now", password = Factory.OwnerPassword });
        var short20 = await PostAsync(Release(drawdown), new { reason = "ok", password = Factory.OwnerPassword });
        var unknown = await PostAsync(Release(long.MaxValue), new { reason = "ok", password = Factory.OwnerPassword });

        frozen.Status.ShouldBe(HttpStatusCode.Conflict);
        frozen.Json.GetProperty("title").GetString()!.ShouldContain("freeze_needs_reconciliation");
        short20.Status.ShouldBe(HttpStatusCode.Conflict);
        short20.Json.GetProperty("title").GetString()!.ShouldContain("drawdown_review_too_short");
        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.Json.GetProperty("code").GetString().ShouldBe("execution.halt_unknown");
        (await ScalarAsync<long>("SELECT count(*) FROM execution.halt_releases WHERE halt_id IN (@f, @d)", ("f", freeze), ("d", drawdown))).ShouldBe(0);
    }

    [Fact]
    public async Task There_is_no_route_that_edits_deletes_or_clears_a_halt_without_the_owner_and_no_drill_route()
    {
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            using var request = new HttpRequestMessage(method, Halts);
            using var response = await Client.SendAsync(request, Ct);
            ((int)response.StatusCode).ShouldBeOneOf(404, 405);
        }

        using var drill = await Client.PostAsJsonAsync(new Uri("/api/execution/drills", UriKind.Relative), new { accountId = Guid.NewGuid() }, Ct);
        ((int)drill.StatusCode).ShouldBeOneOf(404, 405);
    }
}
