using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Selection;

/// <summary>
/// BK-10 criteria 8, 9 and 10 end to end on a real database: GET and PUT /api/selection/sizing-settings and /api/selection/basket-risk-budgets. A fresh database says OFF with no budgets; every change needs the session, the CSRF token and a fresh password; the actor is the session's owner and a body cannot
/// name one; each key that changes is a row with its direction; a loosening shows as the latest loosening; the study is read only and refuses a bad run key with 422 reports.invalid.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class SizingApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Settings = new("/api/selection/sizing-settings", UriKind.Relative);

    private static readonly Uri Budgets = new("/api/selection/basket-risk-budgets", UriKind.Relative);

    private async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(string path, object body)
    {
        using var response = await Client.PutJsonAsync(path, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> ChangeAsync(string? password, bool? vol = null, double? floor = null, bool? cap = null, double? pct = null) =>
        PutAsync(Settings.OriginalString, new { volTargetEnabled = vol, floor, capacityEnabled = cap, capacityPct = pct, password, actor = "owner:evil@example.test" });

    private Task<long> RowsAsync(string table) => ScalarAsync<long>($"SELECT count(*) FROM selection.{table}");

    private async Task<Guid> BasketAsync(string name)
    {
        using var created = await Client.PostJsonAsync("/api/baskets", new { name }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Without_a_session_every_route_is_401_and_nothing_is_written()
    {
        var anonymous = Factory.CreateAnonymousClient();

        using var read = await anonymous.GetAsync(Settings, Ct);
        using var readBudgets = await anonymous.GetAsync(Budgets, Ct);
        using var write = await anonymous.PutAsJsonAsync(Settings, new { volTargetEnabled = true, password = Factory.OwnerPassword }, Ct);
        using var writeBudget = await anonymous.PutAsJsonAsync(new Uri($"/api/selection/basket-risk-budgets/{Guid.NewGuid()}", UriKind.Relative), new { budgetPct = 30, password = Factory.OwnerPassword }, Ct);

        new[] { read, readBudgets, write, writeBudget }.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Unauthorized);
        (await RowsAsync("sizing_settings_changes")).ShouldBe(0);
        (await RowsAsync("basket_risk_budgets")).ShouldBe(0);
    }

    [Fact]
    public async Task A_fresh_database_says_everything_off_and_neutral_and_no_budget()
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(Settings, Ct);
        var budgets = await Client.GetFromJsonAsync<JsonElement>(Budgets, Ct);

        view.GetProperty("volTargetEnabled").GetBoolean().ShouldBeFalse();
        view.GetProperty("capacityEnabled").GetBoolean().ShouldBeFalse();
        view.GetProperty("floor").GetDouble().ShouldBe(0.5);
        view.GetProperty("capacityPct").GetDouble().ShouldBe(0.01);
        view.GetProperty("version").GetInt64().ShouldBe(0);
        view.GetProperty("latestLoosening").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("history").GetArrayLength().ShouldBe(0);
        budgets.GetProperty("budgets").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-password")]
    public async Task A_missing_or_wrong_password_is_401_and_nothing_is_written(string? password)
    {
        var change = await ChangeAsync(password, vol: true);

        change.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await RowsAsync("sizing_settings_changes")).ShouldBe(0);
    }

    [Fact]
    public async Task The_right_password_appends_a_row_per_changed_key_for_the_session_owner_with_its_direction_and_the_database_time()
    {
        var result = await ChangeAsync(Factory.OwnerPassword, vol: true, cap: true, floor: 0.75);

        result.Status.ShouldBe(HttpStatusCode.OK);
        (await RowsAsync("sizing_settings_changes")).ShouldBe(3);
        (await ScalarAsync<long>("SELECT count(*) FROM selection.sizing_settings_changes WHERE changed_by LIKE '%evil%'")).ShouldBe(0, "a body cannot name the actor");
        (await ScalarAsync<long>("SELECT count(*) FROM selection.sizing_settings_changes WHERE changed_by LIKE 'owner:%' AND direction = 'tighten' AND length(hash) = 64")).ShouldBe(3);
        (await ScalarAsync<bool>("SELECT bool_and(changed_at > now() - interval '1 minute' AND changed_at <= now() + interval '1 minute') FROM selection.sizing_settings_changes")).ShouldBeTrue();
        var view = await Client.GetFromJsonAsync<JsonElement>(Settings, Ct);
        view.GetProperty("volTargetEnabled").GetBoolean().ShouldBeTrue();
        view.GetProperty("capacityEnabled").GetBoolean().ShouldBeTrue();
        view.GetProperty("floor").GetDouble().ShouldBe(0.75);
        view.GetProperty("history").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task Turning_a_switch_off_again_asks_for_the_password_again_and_is_the_latest_loosening()
    {
        await ChangeAsync(Factory.OwnerPassword, vol: true);
        var withoutPassword = await ChangeAsync(null, vol: false);
        var off = await ChangeAsync(Factory.OwnerPassword, vol: false);

        withoutPassword.Status.ShouldBe(HttpStatusCode.Unauthorized);
        off.Status.ShouldBe(HttpStatusCode.OK);
        var latest = off.Body.GetProperty("latestLoosening");
        latest.GetProperty("key").GetString().ShouldBe("sizing.vol_target.enabled");
        latest.GetProperty("direction").GetString().ShouldBe("loosen");
        off.Body.GetProperty("volTargetEnabled").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(1.5)]
    public async Task A_floor_outside_a_quarter_to_one_is_400_selection_invalid_and_nothing_is_written(double floor)
    {
        var result = await ChangeAsync(Factory.OwnerPassword, floor: floor);

        result.Status.ShouldBe(HttpStatusCode.BadRequest);
        result.Body.GetProperty("code").GetString().ShouldBe("selection.invalid");
        (await RowsAsync("sizing_settings_changes")).ShouldBe(0);
    }

    [Fact]
    public async Task A_budget_is_set_raised_and_removed_by_the_owner_with_the_password_and_the_history_keeps_every_step()
    {
        var basket = await BasketAsync("Risky");
        var path = $"/api/selection/basket-risk-budgets/{basket}";

        var noPassword = await PutAsync(path, new { budgetPct = 30 });
        var set = await PutAsync(path, new { budgetPct = 30, password = Factory.OwnerPassword });
        var raised = await PutAsync(path, new { budgetPct = 45.5, password = Factory.OwnerPassword });
        var removed = await PutAsync(path, new { budgetPct = (double?)null, password = Factory.OwnerPassword });

        noPassword.Status.ShouldBe(HttpStatusCode.Unauthorized);
        set.Status.ShouldBe(HttpStatusCode.OK);
        set.Body.GetProperty("budgets")[0].GetProperty("budgetPct").GetDouble().ShouldBe(30);
        raised.Body.GetProperty("budgets")[0].GetProperty("budgetPct").GetDouble().ShouldBe(45.5);
        removed.Body.GetProperty("budgets").GetArrayLength().ShouldBe(0);
        removed.Body.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("direction").GetString()).ShouldBe(["loosen", "loosen", "tighten"]);
        (await RowsAsync("basket_risk_budgets")).ShouldBe(3);
        (await ScalarAsync<long>("SELECT count(*) FROM selection.basket_risk_budgets WHERE changed_by LIKE 'owner:%'")).ShouldBe(3);
    }

    [Fact]
    public async Task A_budget_for_a_basket_that_does_not_exist_is_404_and_one_outside_one_to_one_hundred_is_400()
    {
        var basket = await BasketAsync("Real");

        var missing = await PutAsync($"/api/selection/basket-risk-budgets/{Guid.NewGuid()}", new { budgetPct = 30, password = Factory.OwnerPassword });
        var tooSmall = await PutAsync($"/api/selection/basket-risk-budgets/{basket}", new { budgetPct = 0.5, password = Factory.OwnerPassword });

        missing.Status.ShouldBe(HttpStatusCode.NotFound);
        tooSmall.Status.ShouldBe(HttpStatusCode.BadRequest);
        (await RowsAsync("basket_risk_budgets")).ShouldBe(0);
    }

    [Fact]
    public async Task The_study_refuses_a_bad_run_key_with_422_reports_invalid_and_answers_an_unknown_run_with_no_trades()
    {
        using var bad = await Client.GetAsync("/api/reports/sizing-study?run=nothex", Ct);
        using var unknown = await Client.GetAsync($"/api/reports/sizing-study?run={new string('b', 64)}", Ct);

        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await bad.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("reports.invalid");
        unknown.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await unknown.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("result").GetProperty("trades").GetInt32().ShouldBe(0);
        body.GetProperty("caveat").GetString()!.ShouldContain("descriptive, before costs, same trades");
    }
}
