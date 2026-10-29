using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>Spec P12-04 criterion 11 on a real database: the alarms are read only, need a session, come newest first, filter by open and account, and are capped. The alarm record is insert-only, so each test uses its own account id.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExecutionAlarmsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Alarms = new("/api/execution/alarms", UriKind.Relative);

    private async Task<long> RaiseAsync(Guid account, string code, string severity, string detail, int secondsAgo)
    {
        await using var connection = await OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("INSERT INTO execution.alarms (at, code, severity, account_id, detail) VALUES (@at, @code, @severity, @account, @detail) RETURNING id", connection);
        command.Parameters.AddWithValue("at", Now.AddSeconds(-secondsAgo));
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("severity", severity);
        command.Parameters.AddWithValue("account", account);
        command.Parameters.AddWithValue("detail", detail);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task ClearAsync(long alarmId, string how)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO execution.alarm_clears (alarm_id, at, cleared_by, how) VALUES (@id, now(), 'system:protection', @how)", ("id", alarmId), ("how", how));
    }

    [Fact]
    public async Task Without_a_session_the_alarms_are_401()
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(Alarms, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_alarms_come_newest_first_with_their_clear_and_the_open_filter_keeps_only_those_not_cleared()
    {
        var account = Guid.NewGuid();
        var oldest = await RaiseAsync(account, "protection_pending", "warn", "first", 30);
        var middle = await RaiseAsync(account, "protection_breach", "critical", "second", 20);
        await RaiseAsync(account, "oversold", "critical", "third", 10);
        await ClearAsync(oldest, "auto");
        await RaiseAsync(Guid.NewGuid(), "exit_failed", "critical", "someone else", 5);

        var all = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution/alarms?accountId={account}", UriKind.Relative), Ct);
        var open = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution/alarms?accountId={account}&open=true", UriKind.Relative), Ct);

        all.EnumerateArray().Select(a => a.GetProperty("detail").GetString()).ShouldBe(["third", "second", "first"]);
        var first = all.EnumerateArray().Last();
        first.GetProperty("code").GetString().ShouldBe("protection_pending");
        first.GetProperty("severity").GetString().ShouldBe("warn");
        first.GetProperty("clearedBy").GetString().ShouldBe("system:protection");
        first.GetProperty("clearedAt").ValueKind.ShouldBe(JsonValueKind.String);
        all.EnumerateArray().First().GetProperty("clearedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        all.EnumerateArray().First().GetProperty("intentPublicId").ValueKind.ShouldBe(JsonValueKind.Null);
        open.EnumerateArray().Select(a => a.GetProperty("detail").GetString()).ShouldBe(["third", "second"]);
        _ = middle;
    }

    [Fact]
    public async Task Take_is_capped_at_200_and_a_value_outside_1_to_200_is_a_400()
    {
        var account = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            await RaiseAsync(account, "send_unknown", "warn", $"n{i}", 3 - i);
        }

        var two = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution/alarms?accountId={account}&take=2", UriKind.Relative), Ct);

        two.GetArrayLength().ShouldBe(2);
        foreach (var take in new[] { 0, 201, -1 })
        {
            using var response = await Client.GetAsync(new Uri($"/api/execution/alarms?take={take}", UriKind.Relative), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task There_is_no_write_route_for_alarms()
    {
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            using var request = new HttpRequestMessage(method, Alarms);
            using var response = await Client.SendAsync(request, Ct);
            ((int)response.StatusCode).ShouldBeOneOf(404, 405);
        }
    }
}
