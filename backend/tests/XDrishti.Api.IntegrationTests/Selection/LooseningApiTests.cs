using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Selection;

/// <summary>
/// BK-10b end to end on a real database: the loosening banner is part of the plan read and never stored in the plan; the dismissal is an insert-only owner act without a password; the automatic setting is OFF by default and every change needs a fresh password;
/// the database refuses an automatic row whose setting is not the newest one or is OFF, and a second dismissal of one change.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LooseningApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateTimeOffset Changed = new(2099, 3, 1, 6, 0, 0, TimeSpan.Zero);

    private static readonly Uri Settings = new("/api/plans/loosening-settings", UriKind.Relative);

    private Task<long> PlanAsync(DateTimeOffset createdAt) =>
        ScalarAsync<long>(
            "INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES (@k, '2099-03-04', @c, (SELECT COALESCE(max(version), 0) + 1 FROM trading.plans WHERE for_date = '2099-03-04'), repeat('a', 64), 'cell statistics only', true, 'n', 'l', 'probe', @at) RETURNING id",
            ("k", "lo-" + Guid.NewGuid().ToString("N")), ("c", new DateTimeOffset(2099, 3, 4, 3, 35, 0, TimeSpan.Zero)), ("at", createdAt));

    private Task<long> SizingAsync(DateTimeOffset at, string direction = "loosen") =>
        ScalarAsync<long>("INSERT INTO selection.sizing_settings_changes (key, old_value, new_value, direction, changed_by, changed_at, hash) VALUES ('sizing.vol_target.floor', '0.5', '0.7', @d, 'owner:me@x', @at, 'h') RETURNING id", ("d", direction), ("at", at));

    private Task<long> BasketSettingAsync(DateTimeOffset at) =>
        ScalarAsync<long>("INSERT INTO selection.basket_settings_changes (key, old_value, new_value, direction, changed_by, changed_at, hash) VALUES ('gate.breadth_source', 'tracked', 'basket:x', 'loosen', 'owner:me@x', @at, 'h') RETURNING id", ("at", at));

    private async Task<(long Id, Guid Basket)> BudgetAsync(DateTimeOffset at)
    {
        var basket = Guid.NewGuid();
        await ScalarAsync<long>("INSERT INTO market.baskets (id, name, colour, purpose, position, created_at, revision) VALUES (@b, 'Momentum', '#112233', 'watch', 0, now(), 1) RETURNING 1::bigint", ("b", basket));
        await ScalarAsync<long>("INSERT INTO selection.basket_risk_budgets (basket_id, budget_pct, direction, changed_by, changed_at, hash) VALUES (@b, 20, 'tighten', 'owner:me@x', @at, 'h') RETURNING id", ("b", basket), ("at", at.AddMinutes(-5)));
        var id = await ScalarAsync<long>("INSERT INTO selection.basket_risk_budgets (basket_id, budget_pct, direction, changed_by, changed_at, hash) VALUES (@b, NULL, 'loosen', 'owner:me@x', @at, 'h') RETURNING id", ("b", basket), ("at", at));
        return (id, basket);
    }

    private async Task<JsonElement> PlanReadAsync(long id)
    {
        using var response = await Client.GetAsync(new Uri($"/api/plans/{id}", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).Clone();
    }

    private static string[] Ids(JsonElement plan) => [.. plan.GetProperty("loosenings").EnumerateArray().Select(l => l.GetProperty("noticeId").GetString()!)];

    private async Task<HttpStatusCode> DismissAsync(string noticeId)
    {
        using var response = await Client.PostAsync($"/api/plans/loosenings/{noticeId}/dismiss", Ct);
        return response.StatusCode;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> ChangeSettingAsync(string? password, bool auto, long version)
    {
        using var response = await Client.PutJsonAsync(Settings.OriginalString, new { autoDismiss = auto, expectedVersion = version, password, actor = "owner:evil@example.test" }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task Without_a_session_every_route_is_401_and_nothing_is_written()
    {
        var anonymous = Factory.CreateAnonymousClient();

        using var read = await anonymous.GetAsync(Settings, Ct);
        using var write = await anonymous.PutAsJsonAsync(Settings, new { autoDismiss = true, expectedVersion = 0, password = Factory.OwnerPassword }, Ct);
        using var dismiss = await anonymous.PostAsync(new Uri("/api/plans/loosenings/sizing:1/dismiss", UriKind.Relative), null, Ct);

        new[] { read, write, dismiss }.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Unauthorized);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.loosening_settings")).ShouldBe(0);
    }

    [Fact]
    public async Task A_plan_shows_every_loosening_oldest_first_never_a_tighten_and_one_changed_after_it_waits_for_the_next_plan()
    {
        var sizing = await SizingAsync(Changed.AddHours(2));
        await SizingAsync(Changed.AddHours(3), "tighten");
        var basketSetting = await BasketSettingAsync(Changed.AddHours(1));
        var (budget, basketId) = await BudgetAsync(Changed.AddHours(4));
        var plan = await PlanAsync(Changed.AddHours(5));
        var later = await SizingAsync(Changed.AddHours(6));
        var next = await PlanAsync(Changed.AddHours(7));

        var read = await PlanReadAsync(plan);
        var nextRead = await PlanReadAsync(next);

        Ids(read).ShouldBe([$"basket:{basketSetting}", $"sizing:{sizing}", $"budget:{budget}"]);
        var first = read.GetProperty("loosenings")[0];
        first.GetProperty("source").GetString().ShouldBe("basket");
        first.GetProperty("key").GetString().ShouldBe("gate.breadth_source");
        first.GetProperty("oldValue").GetString().ShouldBe("tracked");
        first.GetProperty("newValue").GetString().ShouldBe("basket:x");
        first.GetProperty("actor").GetString().ShouldBe("owner:me@x");
        first.GetProperty("sentence").GetString().ShouldBe("Loosened since 2099-03-01: gate.breadth_source tracked → basket:x (by owner:me@x).");
        var budgetLine = read.GetProperty("loosenings")[2];
        budgetLine.GetProperty("key").GetString().ShouldBe("basket budget Momentum");
        budgetLine.GetProperty("basketId").GetGuid().ShouldBe(basketId);
        budgetLine.GetProperty("oldValue").GetString().ShouldBe("20%");
        budgetLine.GetProperty("newValue").GetString().ShouldBe("off");
        Ids(nextRead).ShouldBe([$"basket:{basketSetting}", $"sizing:{sizing}", $"budget:{budget}", $"sizing:{later}"]);
        read.GetProperty("loosenings").GetArrayLength().ShouldBe(3);
        read.GetProperty("looseningsUnavailable").GetBoolean().ShouldBeFalse();
        var listed = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/plans?limit=10", UriKind.Relative), Ct);
        listed.EnumerateArray().Single(p => p.GetProperty("id").GetInt64() == plan).GetProperty("loosenings").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task The_banner_is_computed_on_read_and_never_changes_the_stored_plan_or_its_hash()
    {
        var plan = await PlanAsync(Changed.AddHours(5));
        var before = await ScalarAsync<string>("SELECT concat_ws('|', input_hash, basis, notice, limits, basis_flags_json::text) FROM trading.plans WHERE id = @p", ("p", plan));
        var bare = await PlanReadAsync(plan);

        var sizing = await SizingAsync(Changed);
        var withBanner = await PlanReadAsync(plan);
        (await DismissAsync($"sizing:{sizing}")).ShouldBe(HttpStatusCode.NoContent);

        bare.GetProperty("loosenings").GetArrayLength().ShouldBe(0);
        withBanner.GetProperty("loosenings").GetArrayLength().ShouldBe(1);
        (await ScalarAsync<string>("SELECT concat_ws('|', input_hash, basis, notice, limits, basis_flags_json::text) FROM trading.plans WHERE id = @p", ("p", plan))).ShouldBe(before);
        withBanner.GetProperty("basis").GetString().ShouldBe(bare.GetProperty("basis").GetString());
    }

    [Fact]
    public async Task Dismissing_is_an_insert_only_owner_act_that_hides_the_change_on_every_plan_and_a_newer_loosening_shows_again()
    {
        var old = await SizingAsync(Changed);
        var older = await PlanAsync(Changed.AddHours(1));
        var newer = await SizingAsync(Changed.AddHours(2));
        var latest = await PlanAsync(Changed.AddHours(3));

        (await DismissAsync($"sizing:{old}")).ShouldBe(HttpStatusCode.NoContent);
        (await DismissAsync($"sizing:{old}")).ShouldBe(HttpStatusCode.NoContent);

        (await ScalarAsync<long>("SELECT count(*) FROM trading.loosening_dismissals")).ShouldBe(1, "a second dismissal adds nothing");
        (await ScalarAsync<string>("SELECT actor FROM trading.loosening_dismissals")).ShouldBe("owner:" + ApiFactory.OwnerEmail);
        (await ScalarAsync<long>("SELECT count(*) FROM selection.sizing_settings_changes WHERE id = @i AND direction = 'loosen'", ("i", old))).ShouldBe(1, "the change row is never edited");
        Ids(await PlanReadAsync(older)).ShouldBeEmpty("a dismissal is of the change: older plans lose it too");
        Ids(await PlanReadAsync(latest)).ShouldBe([$"sizing:{newer}"]);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'LooseningDismissed' AND actor = @a", ("a", "owner:" + ApiFactory.OwnerEmail))).ShouldBe(1, "one audit event, for the first dismissal only");
    }

    [Fact]
    public async Task A_budget_loosening_is_dismissed_by_its_own_notice_id()
    {
        var (budget, _) = await BudgetAsync(Changed);
        var plan = await PlanAsync(Changed.AddHours(1));

        Ids(await PlanReadAsync(plan)).ShouldBe([$"budget:{budget}"]);
        (await DismissAsync($"budget:{budget}")).ShouldBe(HttpStatusCode.NoContent);
        Ids(await PlanReadAsync(plan)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("nonsense", HttpStatusCode.UnprocessableEntity)]
    [InlineData("sizing:0", HttpStatusCode.UnprocessableEntity)]
    [InlineData("crossing:3", HttpStatusCode.UnprocessableEntity)]
    [InlineData("sizing:999999", HttpStatusCode.NotFound)]
    public async Task A_malformed_or_unknown_notice_is_refused_and_nothing_is_written(string noticeId, HttpStatusCode expected)
    {
        (await DismissAsync(noticeId)).ShouldBe(expected);

        (await ScalarAsync<long>("SELECT count(*) FROM trading.loosening_dismissals")).ShouldBe(0);
    }

    [Fact]
    public async Task A_tighten_row_cannot_be_dismissed()
    {
        var tighten = await SizingAsync(Changed, "tighten");

        (await DismissAsync($"sizing:{tighten}")).ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await ScalarAsync<long>("SELECT count(*) FROM trading.loosening_dismissals")).ShouldBe(0);
    }

    [Fact]
    public async Task The_automatic_setting_is_OFF_with_no_row_and_every_change_needs_a_fresh_password()
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(Settings, Ct);
        view.GetProperty("autoDismiss").GetBoolean().ShouldBeFalse();
        view.GetProperty("version").GetInt64().ShouldBe(0);

        (await ChangeSettingAsync(null, true, 0)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await ChangeSettingAsync("not-the-password", true, 0)).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.loosening_settings")).ShouldBe(0);

        var on = await ChangeSettingAsync(Factory.OwnerPassword, true, 0);
        on.Status.ShouldBe(HttpStatusCode.OK);
        on.Body.GetProperty("autoDismiss").GetBoolean().ShouldBeTrue();
        var version = on.Body.GetProperty("version").GetInt64();
        (await ChangeSettingAsync(null, false, version)).Status.ShouldBe(HttpStatusCode.Forbidden, "OFF needs the password too");
        (await ChangeSettingAsync(Factory.OwnerPassword, false, 0)).Status.ShouldBe(HttpStatusCode.Conflict, "a stale version is refused");
        (await ChangeSettingAsync(Factory.OwnerPassword, false, version)).Status.ShouldBe(HttpStatusCode.OK);

        (await ScalarAsync<long>("SELECT count(*) FROM trading.loosening_settings WHERE changed_by = @a AND length(hash) = 64", ("a", "owner:" + ApiFactory.OwnerEmail))).ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'LooseningSettingsChanged'")).ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'LooseningSettingsChanged' AND detail LIKE '%' || @pw || '%'", ("pw", Factory.OwnerPassword))).ShouldBe(0, "never the password");
    }

    private async Task<long> SettingRowAsync(bool auto) =>
        await ScalarAsync<long>("INSERT INTO trading.loosening_settings (auto_dismiss, changed_by, hash) VALUES (@a, 'owner:me@x', @h) RETURNING id", ("a", auto), ("h", auto ? "on-hash" : "off-hash"));

    private async Task<PostgresException> AutoRowAsync(long notice, long? setting, string? hash)
    {
        await using var connection = await OpenAsync();
        await using var insert = new NpgsqlCommand("INSERT INTO trading.loosening_dismissals (notice_source, notice_id, actor, setting_id, setting_hash) VALUES ('sizing', @n, 'auto', @s, @h)", connection);
        insert.Parameters.AddWithValue("n", notice);
        insert.Parameters.AddWithValue("s", (object?)setting ?? DBNull.Value);
        insert.Parameters.AddWithValue("h", (object?)hash ?? DBNull.Value);
        return await Should.ThrowAsync<PostgresException>(async () => await insert.ExecuteNonQueryAsync(Ct));
    }

    [Fact]
    public async Task The_database_refuses_an_automatic_row_with_a_stale_or_OFF_setting_or_none_and_a_second_row_per_change()
    {
        var on = await SettingRowAsync(true);
        var off = await SettingRowAsync(false);

        (await AutoRowAsync(1, off, "off-hash")).SqlState.ShouldBe("23514", "the newest row is OFF");
        (await AutoRowAsync(2, on, "on-hash")).SqlState.ShouldBe("23514", "a stale setting row");
        (await AutoRowAsync(3, null, null)).SqlState.ShouldBe("23514", "auto without a setting row");
        var newest = await SettingRowAsync(true);
        (await AutoRowAsync(4, newest, "wrong-hash")).SqlState.ShouldBe("23514", "the hash must be the setting's");
        (await ScalarAsync<long>("SELECT count(*) FROM trading.loosening_dismissals")).ShouldBe(0);

        await ScalarAsync<long>("INSERT INTO trading.loosening_dismissals (notice_source, notice_id, actor, setting_id, setting_hash) VALUES ('sizing', 5, 'auto', @s, 'on-hash') RETURNING id", ("s", newest));
        (await AutoRowAsync(5, newest, "on-hash")).SqlState.ShouldBe("23505", "one dismissal per change");
        await using var connection = await OpenAsync();
        await using var owner = new NpgsqlCommand("INSERT INTO trading.loosening_dismissals (notice_source, notice_id, actor) VALUES ('sizing', 6, 'owner:me@x'), ('budget', 6, 'someone')", connection);
        (await Should.ThrowAsync<PostgresException>(async () => await owner.ExecuteNonQueryAsync(Ct))).SqlState.ShouldBe("23514", "an owner row must name an owner");
    }
}
