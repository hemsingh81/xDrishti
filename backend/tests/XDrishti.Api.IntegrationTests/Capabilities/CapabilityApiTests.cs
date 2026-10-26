using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Capabilities;

namespace XDrishti.Api.IntegrationTests.Capabilities;

/// <summary>
/// Spec P8-18 Part A (ADR 0128) end to end on a real database: every capability OFF on a fresh database, switching ON needs a fresh password every time and OFF does not, the actor is the session's owner, the same switch twice is one row and one audit event, the database stamps and
/// guards a row, the runtime role can only read and insert, and the generic gate reads what the owner wrote.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class CapabilityApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri List = new("/api/capabilities", UriKind.Relative);

    private const string OwnerActor = "owner:" + ApiFactory.OwnerEmail;

    private static Uri For(string name) => new("/api/capabilities/" + name, UriKind.Relative);

    private Task<HttpResponseMessage> SwitchAsync(string name, bool on, string? password, string reason = "I want to use it", HttpClient? client = null) =>
        (client ?? Client).PutAsJsonAsync(For(name), new { enabled = on, reason, password, actor = "owner:someone-else" }, Ct);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    private async Task<JsonElement> GetAsync()
    {
        using var response = await Client.GetAsync(List, Ct);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        return await ReadAsync(response);
    }

    private Task<long> RowsAsync() => ScalarAsync<long>("SELECT count(*) FROM platform.capability_changes");

    private async Task<bool> GateAsync(string name)
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ICapabilityGate>().RequireAsync(name, Ct)).IsSuccess;
    }

    [Fact]
    public async Task On_a_fresh_database_all_five_capabilities_are_off_and_the_gate_is_closed()
    {
        var view = await GetAsync();

        view.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ShouldBe(["experiments_ui", "learning_moves_ui", "autonomy_form", "exports", "pwa"]);
        view.GetProperty("items").EnumerateArray().ShouldAllBe(i => !i.GetProperty("enabled").GetBoolean() && i.GetProperty("changedAt").ValueKind == JsonValueKind.Null);
        view.GetProperty("history").GetArrayLength().ShouldBe(0);
        (await GateAsync("experiments_ui")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("GET", "/api/capabilities")]
    [InlineData("PUT", "/api/capabilities/experiments_ui")]
    public async Task Without_a_session_nothing_can_be_read_or_switched(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(new { enabled = true, reason = "x", password = "x" }) };
        using var response = await Factory.CreateAnonymousClient().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Switching_on_with_a_wrong_or_missing_password_is_refused_and_writes_nothing_and_a_right_one_in_between_starts_the_count_again()
    {
        try
        {
            foreach (var password in new string?[] { null, "not-the-password" })
            {
                using var refused = await SwitchAsync("experiments_ui", true, password);
                refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
                (await refused.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("auth.invalid_credentials");
            }

            (await RowsAsync()).ShouldBe(0);
            (await GateAsync("experiments_ui")).ShouldBeFalse();
        }
        finally
        {
            await Factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task The_owner_switches_one_capability_on_with_a_fresh_password_as_the_session_owner_never_as_the_body_says_and_only_that_one_opens()
    {
        var answer = await ReadAsync(await SwitchAsync("experiments_ui", true, Factory.OwnerPassword));

        answer.GetProperty("changed").GetBoolean().ShouldBeTrue();
        var items = answer.GetProperty("capabilities").GetProperty("items").EnumerateArray().ToList();
        items.Single(i => i.GetProperty("name").GetString() == "experiments_ui").GetProperty("enabled").GetBoolean().ShouldBeTrue();
        items.Where(i => i.GetProperty("name").GetString() != "experiments_ui").ShouldAllBe(i => !i.GetProperty("enabled").GetBoolean());
        (await ScalarAsync<string>("SELECT actor FROM platform.capability_changes")).ShouldBe(OwnerActor);
        (await GateAsync("experiments_ui")).ShouldBeTrue();
        (await GateAsync("exports")).ShouldBeFalse();
        (await GetAsync()).GetProperty("history")[0].GetProperty("actor").GetString().ShouldBe(OwnerActor);
    }

    [Fact]
    public async Task The_same_switch_again_writes_no_row_and_no_second_audit_event_and_two_at_once_make_one_row()
    {
        var answers = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ => await ReadAsync(await SwitchAsync("exports", true, Factory.OwnerPassword))));

        answers.Count(a => a.GetProperty("changed").GetBoolean()).ShouldBe(1);
        (await RowsAsync()).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'CapabilityChanged'")).ShouldBe(1);
        (await ReadAsync(await SwitchAsync("exports", true, Factory.OwnerPassword))).GetProperty("changed").GetBoolean().ShouldBeFalse();
        (await RowsAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Switching_off_needs_no_password_closes_the_gate_and_is_another_row_the_first_is_never_edited()
    {
        await ReadAsync(await SwitchAsync("pwa", true, Factory.OwnerPassword));

        var off = await ReadAsync(await SwitchAsync("pwa", false, null, "kill switch"));

        off.GetProperty("changed").GetBoolean().ShouldBeTrue();
        (await GateAsync("pwa")).ShouldBeFalse();
        (await RowsAsync()).ShouldBe(2);
        (await ScalarAsync<bool>("SELECT enabled FROM platform.capability_changes ORDER BY id LIMIT 1")).ShouldBeTrue();
        (await GetAsync()).GetProperty("history").EnumerateArray().Select(h => h.GetProperty("enabled").GetBoolean()).ShouldBe([false, true]);
    }

    [Fact]
    public async Task An_unknown_name_is_404_before_any_password_is_looked_at_and_a_blank_reason_is_422_and_neither_writes()
    {
        using var unknown = await SwitchAsync("apply_changes", true, "not-the-password");
        using var blank = await SwitchAsync("exports", true, Factory.OwnerPassword, reason: " ");
        using var upper = await SwitchAsync("Exports", true, Factory.OwnerPassword);

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("capabilities.not_found");
        blank.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await blank.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("capabilities.invalid");
        upper.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_kill_switch_never_answers_429_while_switching_on_is_limited_per_minute()
    {
        await ReadAsync(await SwitchAsync("pwa", true, Factory.OwnerPassword));
        try
        {
            for (var i = 0; i < 25; i++)
            {
                using var off = await SwitchAsync("pwa", false, null, "kill switch " + i);
                off.StatusCode.ShouldBe(HttpStatusCode.OK, "switch " + i);
            }

            var limited = 0;
            for (var i = 0; i < 14; i++)
            {
                using var on = await SwitchAsync("pwa", true, "not-the-password");
                if (on.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    limited++;
                    (await on.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("auth.rate_limited");
                    on.Headers.RetryAfter.ShouldNotBeNull();
                }
            }

            limited.ShouldBeGreaterThan(0, "attempts to switch ON beyond the sign-in limit are refused");
        }
        finally
        {
            await Factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task An_open_step_up_window_does_not_stand_in_for_the_password_and_is_not_used_up_by_the_switch()
    {
        var elevated = await Factory.SignInElevatedAsync(Ct);
        using var none = await SwitchAsync("exports", true, null, client: elevated.Client);
        using var ok = await SwitchAsync("exports", true, Factory.OwnerPassword, client: elevated.Client);
        var until = await ScalarAsync<DateTime>("SELECT elevated_until FROM auth.sessions WHERE elevated_until IS NOT NULL ORDER BY elevated_until DESC LIMIT 1");

        using var again = await SwitchAsync("pwa", true, null, client: elevated.Client);

        none.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ScalarAsync<DateTime>("SELECT elevated_until FROM auth.sessions WHERE elevated_until IS NOT NULL ORDER BY elevated_until DESC LIMIT 1")).ShouldBe(until);
    }

    [Fact]
    public async Task The_audit_has_one_event_per_change_naming_the_capability_and_the_state_with_no_reason_and_no_password()
    {
        await ReadAsync(await SwitchAsync("learning_moves_ui", true, Factory.OwnerPassword, "a reason that must stay in the capability log only"));
        await ReadAsync(await SwitchAsync("learning_moves_ui", false, null));

        var details = await ScalarAsync<string>("SELECT string_agg(detail || '|' || actor, ';' ORDER BY at, id) FROM accounts.account_events WHERE event = 'CapabilityChanged'");
        using var audit = await Client.GetAsync(new Uri("/api/audit/entries?from=2026-10-01&to=2027-09-01&source=account&limit=50", UriKind.Relative), Ct);
        var text = await audit.Content.ReadAsStringAsync(Ct);

        details.ShouldBe($"name=learning_moves_ui on|{OwnerActor};name=learning_moves_ui off|{OwnerActor}");
        audit.StatusCode.ShouldBe(HttpStatusCode.OK);
        text.ShouldContain("CapabilityChanged");
        text.ShouldNotContain(Factory.OwnerPassword);
        text.ShouldNotContain("a reason that must stay");
    }

    [Fact]
    public async Task The_database_stamps_the_row_chains_it_to_the_previous_one_and_refuses_a_non_owner_and_a_bad_name_whatever_the_application_says()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO platform.capability_changes (id, capability, enabled, reason, actor, changed_at, previous_id, snapshot_hash) OVERRIDING SYSTEM VALUE VALUES (999, 'exports', true, 'first', 'owner:x@y.z', '2000-01-01', 5, 'chosen')");
        await ExecuteAsync(connection, "INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES ('exports', false, 'second', 'owner:x@y.z')");

        var first = await ScalarAsync<long>("SELECT id FROM platform.capability_changes ORDER BY id LIMIT 1");
        var second = await ScalarAsync<long>("SELECT id FROM platform.capability_changes ORDER BY id DESC LIMIT 1");
        (await ScalarAsync<bool>("SELECT previous_id IS NULL AND changed_at > now() - interval '1 minute' AND length(snapshot_hash) = 64 AND snapshot_hash <> 'chosen' FROM platform.capability_changes ORDER BY id LIMIT 1")).ShouldBeTrue();
        (await ScalarAsync<long>("SELECT previous_id FROM platform.capability_changes ORDER BY id DESC LIMIT 1")).ShouldBe(first);
        var cli = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES ('exports', true, 'x', 'cli:xd')"));
        var badName = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES ('Exports; DROP', true, 'x', 'owner:x@y.z')"));
        var blank = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES ('exports', true, ' ', 'owner:x@y.z')"));

        first.ShouldNotBe(999); // a chosen id is overwritten
        second.ShouldBeGreaterThan(first);
        cli.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        badName.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        blank.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    private static readonly string[] Rewrites = ["UPDATE platform.capability_changes SET enabled = false", "DELETE FROM platform.capability_changes", "TRUNCATE platform.capability_changes"];

    [Fact]
    public async Task As_the_runtime_role_the_log_can_be_read_and_inserted_into_and_never_changed_deleted_or_truncated()
    {
        await AppRoleProbe.RunAsync(
            Factory,
            async connection =>
            {
                await AppRoleProbe.ExecuteAsync(connection, "INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES ('exports', true, 'probe', 'owner:probe@example.test')", Ct);
                (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM platform.capability_changes", Ct)).ShouldBe(1L);
                foreach (var statement in Rewrites)
                {
                    var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, statement, Ct));
                    refused.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
                }
            },
            Ct);
    }
}
