using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// BK-11 on a real database: the owner's universe settings (default list, password on every change, one row per changed key, owner actor from the session), the universe trial count of a strategy, the universe lens of the report engine and the
/// refusal of a full Lab test on an unregistered basket.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class UniverseSettingsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Settings = new("/api/universe/settings", UriKind.Relative);

    private async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(object body)
    {
        using var response = await Client.PutJsonAsync(Settings.OriginalString, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<long> RowsAsync() => ScalarAsync<long>("SELECT count(*) FROM research.universe_settings_changes");

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.universe_settings_changes");
    }

    [Fact]
    public async Task Without_a_session_the_routes_are_401_and_nothing_is_written()
    {
        var anonymous = Factory.CreateAnonymousClient();

        using var read = await anonymous.GetAsync(Settings, Ct);
        using var write = await anonymous.PutAsJsonAsync(Settings, new { minTradesChild = 40, password = Factory.OwnerPassword }, Ct);

        read.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        write.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_fresh_database_says_the_defaults_with_the_eight_registered_baskets()
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(Settings, Ct);

        view.GetProperty("minTradesChild").GetInt32().ShouldBe(30);
        view.GetProperty("linkMinTrades").GetInt32().ShouldBe(100);
        view.GetProperty("registeredBaskets").GetArrayLength().ShouldBe(8);
        view.GetProperty("history").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-password")]
    public async Task A_missing_or_wrong_password_is_401_and_nothing_is_written(string? password)
    {
        var change = await PutAsync(new { minTradesChild = 40, password, expectedVersion = 0 });

        change.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_right_password_appends_a_row_per_changed_key_for_the_session_owner_and_a_bad_value_is_422()
    {
        var bad = await PutAsync(new { minTradesChild = 0, password = Factory.OwnerPassword, expectedVersion = 0 });
        var ok = await PutAsync(new { minTradesChild = 45, registeredBaskets = "Nifty 50, My picks", password = Factory.OwnerPassword, expectedVersion = 0, actor = "owner:evil@example.test" });

        bad.Status.ShouldBe(HttpStatusCode.BadRequest);
        ok.Status.ShouldBe(HttpStatusCode.OK);
        (await RowsAsync()).ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(*) FROM research.universe_settings_changes WHERE changed_by LIKE '%evil%'")).ShouldBe(0, "a body cannot name the actor");
        ok.Body.GetProperty("minTradesChild").GetInt32().ShouldBe(45);
        ok.Body.GetProperty("registeredBaskets").EnumerateArray().Select(e => e.GetString()).ShouldBe(["Nifty 50", "My picks"]);
        var again = await Client.GetFromJsonAsync<JsonElement>(Settings, Ct);
        again.GetProperty("history").GetArrayLength().ShouldBe(2);
        again.GetProperty("version").GetInt64().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_change_from_an_old_version_is_409_and_writes_nothing_and_a_missing_version_is_400()
    {
        var first = await PutAsync(new { minTradesChild = 41, password = Factory.OwnerPassword, expectedVersion = 0 });
        var stale = await PutAsync(new { minTradesChild = 42, password = Factory.OwnerPassword, expectedVersion = 0 });
        var missing = await PutAsync(new { minTradesChild = 43, password = Factory.OwnerPassword });
        var current = first.Body.GetProperty("version").GetInt64();
        var fresh = await PutAsync(new { minTradesChild = 44, password = Factory.OwnerPassword, expectedVersion = current });

        first.Status.ShouldBe(HttpStatusCode.OK);
        stale.Status.ShouldBe(HttpStatusCode.Conflict);
        missing.Status.ShouldBe(HttpStatusCode.BadRequest);
        fresh.Status.ShouldBe(HttpStatusCode.OK);
        (await RowsAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task A_strategy_with_no_basket_test_has_one_variant_and_the_report_universe_lens_is_refused_with_the_reason()
    {
        var trials = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/lab/strategies/NOPE/universe-trials", UriKind.Relative), Ct);
        using var lens = await Client.GetAsync(new Uri("/api/reports/groups?lens=universe", UriKind.Relative), Ct);

        trials.GetProperty("baskets").GetInt32().ShouldBe(0);
        trials.GetProperty("extraVariants").GetInt32().ShouldBe(0);
        lens.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await lens.Content.ReadAsStringAsync(Ct)).ShouldContain("reports.dimension_unavailable");
    }
}
