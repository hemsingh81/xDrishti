using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Depth;

/// <summary>
/// P7-16 criterion 6 end to end on a real database: the owner's start switch is a fresh-password act on every change (ON and OFF), the actor is the session, a reason is needed, the rows are insert-only, one audit event names the row and the state and never the reason, and the
/// Services screen shows the recorder paused with no "Run now".
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DepthRecorderApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string SecretReason = "a-reason-that-must-never-reach-the-audit-feed";

    private static readonly Uri Recorder = new("/api/depth/recorder", UriKind.Relative);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "TRUNCATE market.depth_recorder_switch RESTART IDENTITY");
        await ExecuteAsync(connection, "DELETE FROM accounts.account_events WHERE event = 'DepthRecorderSwitched'");
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(string? password, bool enabled = true, string? reason = SecretReason, HttpClient? client = null)
    {
        using var response = await (client ?? Client).PutAsJsonAsync(Recorder, new { enabled, reason, password, actor = "owner:evil@example.test" }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<long> RowsAsync(string where = "true") => ScalarAsync<long>($"SELECT count(*) FROM market.depth_recorder_switch WHERE {where}");

    [Fact]
    public async Task Without_a_session_both_routes_are_401()
    {
        using var read = await Factory.CreateAnonymousClient().GetAsync(Recorder, Ct);
        using var write = await Factory.CreateAnonymousClient().PutAsJsonAsync(Recorder, new { enabled = true, reason = "x", password = Factory.OwnerPassword }, Ct);

        (read.StatusCode, write.StatusCode).ShouldBe((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized));
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_fresh_database_says_off_with_the_reasons_and_an_empty_history()
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(Recorder, Ct);

        view.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        view.GetProperty("switchId").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("history").GetArrayLength().ShouldBe(0);
        view.GetProperty("notRunningBecause").GetArrayLength().ShouldBeGreaterThanOrEqualTo(2);
        view.GetProperty("notice").GetString().ShouldNotBeNull().ShouldContain("nothing reads it");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-password")]
    public async Task A_missing_or_wrong_password_is_401_and_nothing_is_written(string? password)
    {
        var result = await PutAsync(password);

        result.Status.ShouldBe(HttpStatusCode.Unauthorized);
        result.Body.GetProperty("code").GetString().ShouldBe("auth.invalid_credentials");
        (await RowsAsync()).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'DepthRecorderSwitched'")).ShouldBe(0);
    }

    [Fact]
    public async Task The_right_password_and_a_reason_append_a_row_for_the_owner_of_the_session_and_one_audit_event_without_the_reason()
    {
        var result = await PutAsync(Factory.OwnerPassword);

        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Body.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        var id = result.Body.GetProperty("id").GetInt64();
        (await RowsAsync()).ShouldBe(1);
        (await RowsAsync("actor LIKE '%evil%'")).ShouldBe(0, "a body cannot name the actor");
        (await ScalarAsync<string>("SELECT actor FROM market.depth_recorder_switch")).ShouldStartWith("owner:");
        var detail = await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'DepthRecorderSwitched'");
        detail.ShouldBe($"switch={id} depth_recorder=on");
        detail.ShouldNotContain(SecretReason);
        var view = await Client.GetFromJsonAsync<JsonElement>(Recorder, Ct);
        view.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        view.GetProperty("switchId").GetInt64().ShouldBe(id);
        view.GetProperty("history")[0].GetProperty("reason").GetString().ShouldBe(SecretReason);
    }

    [Fact]
    public async Task Every_change_asks_for_the_password_again_ON_and_OFF_and_an_earlier_one_does_not_count()
    {
        var on = await PutAsync(Factory.OwnerPassword, enabled: true);
        var offWithout = await PutAsync(null, enabled: false);
        var off = await PutAsync(Factory.OwnerPassword, enabled: false);
        var onWithout = await PutAsync(null, enabled: true);

        on.Status.ShouldBe(HttpStatusCode.OK);
        offWithout.Status.ShouldBe(HttpStatusCode.Unauthorized);
        off.Status.ShouldBe(HttpStatusCode.OK);
        onWithout.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await ScalarAsync<string>("SELECT string_agg(enabled::text, ',' ORDER BY id) FROM market.depth_recorder_switch")).ShouldBe("true,false");
        (await ScalarAsync<string>("SELECT string_agg(detail, ' | ' ORDER BY at, id) FROM accounts.account_events WHERE event = 'DepthRecorderSwitched'")).ShouldContain("depth_recorder=off");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_missing_reason_with_the_right_password_is_400_and_writes_nothing(string? reason)
    {
        var result = await PutAsync(Factory.OwnerPassword, reason: reason);

        result.Status.ShouldBe(HttpStatusCode.BadRequest);
        result.Body.GetProperty("code").GetString().ShouldBe("depth.reason_invalid");
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_post_is_not_a_route()
    {
        using var response = await Client.PostAsJsonAsync(Recorder, new { enabled = true, reason = "x", password = Factory.OwnerPassword }, Ct);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_service_is_listed_paused_by_default_and_run_now_is_refused()
    {
        var service = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/services/depth-recorder", UriKind.Relative), Ct);
        using var run = await Client.PostAsync(new Uri("/api/services/depth-recorder/run", UriKind.Relative), null, Ct);

        service.GetProperty("id").GetString().ShouldBe("depth-recorder");
        service.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        run.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await run.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("services.read_only");
        (await ScalarAsync<long>("SELECT count(*) FROM platform.service_runs WHERE service_id = 'depth-recorder'")).ShouldBe(0);
    }
}
