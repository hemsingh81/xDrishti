using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Domain.Reporting;

namespace XDrishti.Api.IntegrationTests.Selection;

/// <summary>
/// BK-08 criteria 7, 9, 10 and 11 end to end on a real database: GET and PUT /api/selection/basket-settings and GET /api/market/basket-context. A fresh database says OFF; every change needs the session, the CSRF token and a fresh password; the actor is the session's owner and a body
/// cannot name one; each key that changes is a row with its direction; the context table is insert-once.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class BasketSettingsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Settings = new("/api/selection/basket-settings", UriKind.Relative);

    private async Task<(HttpStatusCode Status, JsonElement Body)> ChangeAsync(string? password, string? source = null, double? cap = null, bool? clear = null, double? rho = null)
    {
        using var response = await Client.PutJsonAsync(Settings.OriginalString, new { breadthSource = source, maxClusterRiskPct = cap, clearCap = clear, clusterRho = rho, password, actor = "owner:evil@example.test" }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<long> RowsAsync() => ScalarAsync<long>("SELECT count(*) FROM selection.basket_settings_changes");

    private async Task<Guid> BasketAsync(string name)
    {
        using var created = await Client.PostJsonAsync("/api/baskets", new { name }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static BasketContextRow Row(Guid basket, DateOnly day, double b20 = 0.625) => new(basket, day, 10, 8, b20, 0.5, null, 0.6, 0.1, 0.012, 0.31, "value", "single_snapshot");

    [Fact]
    public async Task Without_a_session_every_route_is_401_and_nothing_is_written()
    {
        var anonymous = Factory.CreateAnonymousClient();

        using var read = await anonymous.GetAsync(Settings, Ct);
        using var write = await anonymous.PutAsJsonAsync(Settings, new { maxClusterRiskPct = 1.0, password = Factory.OwnerPassword }, Ct);
        using var context = await anonymous.GetAsync(new Uri($"/api/market/basket-context?basket={Guid.NewGuid()}", UriKind.Relative), Ct);

        new[] { read, write, context }.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Unauthorized);
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_fresh_database_says_the_tracked_set_no_cap_and_a_cut_of_0_70()
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(Settings, Ct);

        view.GetProperty("breadthSource").GetString().ShouldBe("tracked");
        view.GetProperty("maxClusterRiskPct").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("clusterRho").GetDouble().ShouldBe(0.70);
        view.GetProperty("version").GetInt64().ShouldBe(0);
        view.GetProperty("latestLoosening").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("history").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-password")]
    public async Task A_missing_or_wrong_password_is_401_and_nothing_is_written(string? password)
    {
        var change = await ChangeAsync(password, cap: 1.0);

        change.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_right_password_appends_a_row_per_changed_key_for_the_session_owner_with_its_direction_and_the_database_time()
    {
        var result = await ChangeAsync(Factory.OwnerPassword, cap: 1.0, rho: 0.8);

        result.Status.ShouldBe(HttpStatusCode.OK);
        (await RowsAsync()).ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(*) FROM selection.basket_settings_changes WHERE changed_by LIKE '%evil%'")).ShouldBe(0, "a body cannot name the actor");
        (await ScalarAsync<long>("SELECT count(*) FROM selection.basket_settings_changes WHERE changed_by LIKE 'owner:%' AND length(hash) = 64")).ShouldBe(2);
        (await ScalarAsync<string>("SELECT direction FROM selection.basket_settings_changes WHERE key = 'selection.max_cluster_risk_pct'")).ShouldBe("tighten");
        (await ScalarAsync<string>("SELECT direction FROM selection.basket_settings_changes WHERE key = 'selection.cluster_rho'")).ShouldBe("loosen");
        (await ScalarAsync<bool>("SELECT bool_and(changed_at > now() - interval '1 minute' AND changed_at <= now() + interval '1 minute') FROM selection.basket_settings_changes")).ShouldBeTrue();
        var view = await Client.GetFromJsonAsync<JsonElement>(Settings, Ct);
        view.GetProperty("maxClusterRiskPct").GetDouble().ShouldBe(1.0);
        view.GetProperty("clusterRho").GetDouble().ShouldBe(0.8);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'BasketSettingsChanged'")).ShouldBe(2, "one audit event per accepted change");
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'BasketSettingsChanged' AND detail LIKE '%' || @pw || '%'", ("pw", Factory.OwnerPassword))).ShouldBe(0, "never the password");
    }

    [Fact]
    public async Task Loosening_asks_for_the_password_again_and_the_cap_off_is_the_latest_loosening()
    {
        await ChangeAsync(Factory.OwnerPassword, cap: 1.0);
        var withoutPassword = await ChangeAsync(null, clear: true);
        var cleared = await ChangeAsync(Factory.OwnerPassword, clear: true);

        withoutPassword.Status.ShouldBe(HttpStatusCode.Unauthorized);
        cleared.Status.ShouldBe(HttpStatusCode.OK);
        cleared.Body.GetProperty("maxClusterRiskPct").ValueKind.ShouldBe(JsonValueKind.Null);
        cleared.Body.GetProperty("latestLoosening").GetProperty("direction").GetString().ShouldBe("loosen");
    }

    [Fact]
    public async Task A_basket_is_chosen_as_the_gate_source_as_a_loosening_and_a_missing_one_is_404()
    {
        var basket = await BasketAsync("Gate basket");

        var missing = await ChangeAsync(Factory.OwnerPassword, source: $"basket:{Guid.NewGuid()}");
        var chosen = await ChangeAsync(Factory.OwnerPassword, source: $"basket:{basket}");

        missing.Status.ShouldBe(HttpStatusCode.NotFound);
        chosen.Status.ShouldBe(HttpStatusCode.OK);
        chosen.Body.GetProperty("breadthSource").GetString().ShouldBe($"basket:{basket}");
        chosen.Body.GetProperty("latestLoosening").GetProperty("key").GetString().ShouldBe("gate.breadth_source");
        (await RowsAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(0.0, null, null)]
    [InlineData(150.0, null, null)]
    [InlineData(null, null, 0.05)]
    public async Task A_value_out_of_range_is_400_selection_invalid_and_nothing_is_written(double? cap, string? source, double? rho)
    {
        var result = await ChangeAsync(Factory.OwnerPassword, source, cap, rho: rho);

        result.Status.ShouldBe(HttpStatusCode.BadRequest);
        result.Body.GetProperty("code").GetString().ShouldBe("selection.invalid");
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_context_table_is_insert_once_and_the_read_returns_the_newest_rows_first()
    {
        var basket = Guid.NewGuid();
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBasketContextStore>();
        var day = new DateOnly(2026, 10, 8);

        (await store.InsertOnceAsync(Row(basket, day), Ct)).ShouldBeTrue();
        (await store.InsertOnceAsync(Row(basket, day, b20: 0.1), Ct)).ShouldBeFalse("a second run for the same date inserts nothing");
        (await store.InsertOnceAsync(Row(basket, day.AddDays(1)), Ct)).ShouldBeTrue();

        (await ScalarAsync<decimal>("SELECT b20 FROM market.basket_context_daily WHERE basket_id = @b AND as_of = @d", ("b", basket), ("d", day))).ShouldBe(0.625m, "the stored row was never updated");
        var read = await Client.GetFromJsonAsync<JsonElement>($"/api/market/basket-context?basket={basket}&from=2026-10-08&to=2026-10-09", Ct);
        read.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("asOf").GetString()).ShouldBe(["2026-10-09", "2026-10-08"]);
        read.GetProperty("items")[1].GetProperty("b20").GetDouble().ShouldBe(0.625);
        read.GetProperty("items")[1].GetProperty("membershipBasis").GetString().ShouldBe("single_snapshot");
        (await store.LatestBeforeAsync(basket, day.AddDays(1), Ct))!.AsOf.ShouldBe(day);
        (await store.LatestBeforeAsync(basket, day, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task The_context_read_needs_a_basket_and_a_sane_range()
    {
        using var noBasket = await Client.GetAsync("/api/market/basket-context", Ct);
        using var inverted = await Client.GetAsync($"/api/market/basket-context?basket={Guid.NewGuid()}&from=2026-10-09&to=2026-10-01", Ct);
        using var empty = await Client.GetAsync($"/api/market/basket-context?basket={Guid.NewGuid()}", Ct);

        noBasket.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        inverted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await empty.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").GetArrayLength().ShouldBe(0);
    }
}
