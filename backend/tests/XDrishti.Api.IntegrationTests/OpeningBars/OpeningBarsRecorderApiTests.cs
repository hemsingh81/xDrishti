using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.OpeningBars;

/// <summary>
/// BK-03b end to end on a real database: the owner's opening-bars switch is a fresh-confirmation act on every change (ON and OFF), the actor is the session, a reason is needed, the rows are insert-only, one audit event names the row and the state and never
/// the reason; and the retention function removes only bars older than the keep days.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OpeningBarsRecorderApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string OwnerReason = "my private reason for the change";

    private static readonly Uri Recorder = new("/api/opening-bars/recorder", UriKind.Relative);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "TRUNCATE market.opening_bars_switch RESTART IDENTITY");
        await ExecuteAsync(connection, "TRUNCATE market.bars_1m_live");
        await ExecuteAsync(connection, "DELETE FROM accounts.account_events WHERE event = 'OpeningBarsSwitched'");
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(string? confirmation, bool enabled = true, string? reason = OwnerReason)
    {
        using var response = await Client.PutAsJsonAsync(Recorder, new { enabled, reason, password = confirmation, actor = "owner:evil@example.test" }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<long> RowsAsync() => ScalarAsync<long>("SELECT count(*) FROM market.opening_bars_switch");

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
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-guess")]
    public async Task A_missing_or_wrong_confirmation_is_401_and_nothing_is_written(string? confirmation)
    {
        var result = await PutAsync(confirmation);

        result.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await RowsAsync()).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'OpeningBarsSwitched'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_right_confirmation_and_a_reason_append_a_row_for_the_owner_of_the_session_and_one_audit_event_without_the_reason()
    {
        var result = await PutAsync(Factory.OwnerPassword);

        result.Status.ShouldBe(HttpStatusCode.OK);
        var id = result.Body.GetProperty("id").GetInt64();
        (await ScalarAsync<string>("SELECT actor FROM market.opening_bars_switch")).ShouldStartWith("owner:");
        (await ScalarAsync<long>("SELECT count(*) FROM market.opening_bars_switch WHERE actor LIKE '%evil%'")).ShouldBe(0, "a body cannot name the actor");
        var detail = await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'OpeningBarsSwitched'");
        detail.ShouldBe($"switch={id} opening_bars=on");
        detail.ShouldNotContain(OwnerReason);
        var view = await Client.GetFromJsonAsync<JsonElement>(Recorder, Ct);
        view.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        view.GetProperty("history")[0].GetProperty("reason").GetString().ShouldBe(OwnerReason);
    }

    [Fact]
    public async Task Every_change_asks_for_the_confirmation_again_ON_and_OFF()
    {
        var on = await PutAsync(Factory.OwnerPassword, enabled: true);
        var offWithout = await PutAsync(null, enabled: false);
        var off = await PutAsync(Factory.OwnerPassword, enabled: false);

        (on.Status, offWithout.Status, off.Status).ShouldBe((HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.OK));
        (await ScalarAsync<string>("SELECT string_agg(enabled::text, ',' ORDER BY id) FROM market.opening_bars_switch")).ShouldBe("true,false");
    }

    [Fact]
    public async Task A_missing_reason_with_the_right_confirmation_is_400_and_writes_nothing()
    {
        var result = await PutAsync(Factory.OwnerPassword, reason: "  ");

        result.Status.ShouldBe(HttpStatusCode.BadRequest);
        result.Body.GetProperty("code").GetString().ShouldBe("opening_bars.reason_invalid");
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_retention_function_removes_only_bars_older_than_the_keep_days_and_refuses_less_than_one_day()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO market.bars_1m_live (instrument_id, minute_start, o, h, l, c, v, packets) VALUES (gen_random_uuid(), now() - interval '100 days', 1, 1, 1, 1, 1, 1), (gen_random_uuid(), now() - interval '2 days', 1, 1, 1, 1, 1, 1)");
        }

        var removed = await ScalarAsync<int>("SELECT market.prune_opening_bars(60)");

        removed.ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM market.bars_1m_live")).ShouldBe(1);
        await Should.ThrowAsync<Npgsql.PostgresException>(async () => await ScalarAsync<int>("SELECT market.prune_opening_bars(0)"));
    }
}
