using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>Spec P12-01b criterion 12 on a real database: the status is read only, needs a session, answers environment none and closed gates on an empty record, and reads a heartbeat as down when it is older than 30 seconds.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExecutionStatusApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Status = new("/api/execution/status", UriKind.Relative);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM platform.service_heartbeats WHERE service_name = 'exec'");
    }

    private async Task SetBeatAsync(string state, int secondsAgo, string details)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, $"INSERT INTO platform.service_heartbeats (service_name, instance, version, started_at, last_seen_at, state, details) VALUES ('exec', 'test', '1', now() - interval '1 hour', '{Now.AddSeconds(-secondsAgo):O}', '{state}', '{details}'::jsonb)");
    }

    [Fact]
    public async Task Without_a_session_the_status_is_401()
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(Status, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_empty_record_is_down_with_environment_none_closed_gates_and_blocked_entries()
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(Status, Ct);

        view.GetProperty("service").GetProperty("state").GetString().ShouldBe("down");
        view.GetProperty("service").GetProperty("lastBeatAt").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("environment").GetString().ShouldBe("none");
        view.GetProperty("halts").GetArrayLength().ShouldBe(0);
        view.GetProperty("openIntents").GetInt32().ShouldBe(0);
        view.GetProperty("entriesBlocked").GetBoolean().ShouldBeTrue();
        foreach (var account in view.GetProperty("accounts").EnumerateArray())
        {
            account.GetProperty("gate").GetProperty("open").GetBoolean().ShouldBeFalse();
            account.GetProperty("gate").GetProperty("reason").GetString().ShouldBe("no_mode_answer");
        }
    }

    [Fact]
    public async Task A_fresh_heartbeat_shows_its_state_environment_and_settings_hash_and_an_old_one_is_down()
    {
        await SetBeatAsync("recovering", 3, """{"environment":"sandbox","settingsHash":"abc","commandsAnswered":2}""");
        var fresh = await Client.GetFromJsonAsync<JsonElement>(Status, Ct);

        fresh.GetProperty("service").GetProperty("state").GetString().ShouldBe("recovering");
        fresh.GetProperty("environment").GetString().ShouldBe("sandbox");
        fresh.GetProperty("settingsHash").GetString().ShouldBe("abc");

        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, $"UPDATE platform.service_heartbeats SET last_seen_at = '{Now.AddSeconds(-31):O}' WHERE service_name = 'exec'");
        }

        var old = await Client.GetFromJsonAsync<JsonElement>(Status, Ct);
        old.GetProperty("service").GetProperty("state").GetString().ShouldBe("down");
        old.GetProperty("environment").GetString().ShouldBe("none");
        old.GetProperty("settingsHash").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_unknown_state_word_never_reads_as_healthy()
    {
        await SetBeatAsync("ready-ish", 1, """{"environment":"live"}""");

        var view = await Client.GetFromJsonAsync<JsonElement>(Status, Ct);

        view.GetProperty("service").GetProperty("state").GetString().ShouldBe("down");
    }
}
