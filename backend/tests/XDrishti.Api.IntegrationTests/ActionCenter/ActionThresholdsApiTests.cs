using System.Net;
using System.Net.Http.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.IntegrationTests.ActionCenter;

/// <summary>The thresholds of the rules through the API (P2-09, criteria 7 and 12): defaults, saving with field errors, "Restore defaults", the row in <c>platform.app_settings</c>, the audit event, and the effect on the next read.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ActionThresholdsApiTests(ApiFactory factory) : ActionCenterTestBase(factory)
{
    private static object Body(decimal belowCost = 12m, decimal high = 22m, decimal gain = 30m, decimal floor = 20m, decimal multiple = 2m, decimal positions = 4m, decimal concentration = 40m, decimal lag = 12m, decimal lagDays = 200m, decimal window = 45m) =>
        new { belowCostPct = belowCost, belowCostHighPct = high, gainPct = gain, overweightFloorPct = floor, overweightMultiple = multiple, minPositions = positions, concentrationPct = concentration, lagPp = lag, lagDays, longTermWindowDays = window };

    private async Task<ActionThresholdsView> GetAsync()
    {
        using var response = await Client.GetAsync("/api/portfolio/action-thresholds", Ct);
        return await response.ReadAsync<ActionThresholdsView>(Ct);
    }

    [Fact]
    public async Task Without_a_stored_row_the_documented_defaults_are_returned()
    {
        var view = await GetAsync();

        view.Values.ShouldBe(new ActionThresholds(10m, 20m, 25m, 15m, 1.5m, 3, 35m, 10m, 180, 30));
        (view.IsDefault, view.UpdatedBy, view.UpdatedAt, view.Defaults).ShouldBe((true, null, null, view.Values));
    }

    [Fact]
    public async Task Saving_stores_one_settings_row_audits_it_and_the_next_read_uses_the_values_at_once()
    {
        // AAA is down 30 %: with the below-cost band moved to 35 % / 50 % it is no longer an item at all.
        (await ActionsAsync()).Items.Any(i => i.Rule == "below_cost").ShouldBeTrue();

        using var saved = await Client.PutJsonAsync("/api/portfolio/action-thresholds", Body(belowCost: 35m, high: 50m), Ct);
        var view = await saved.ReadAsync<ActionThresholdsView>(Ct);

        (view.IsDefault, view.UpdatedBy).ShouldBe((false, ApiFactory.OwnerEmail));
        view.Values.BelowCostPct.ShouldBe(35m);
        (await GetAsync()).Values.ShouldBe(view.Values);
        (await ActionsAsync()).Items.Any(i => i.Rule == "below_cost").ShouldBeFalse();
        (await CountAsync("SELECT count(*) FROM platform.app_settings WHERE key = 'portfolio.action_thresholds'")).ShouldBe(1);
        (await EventsAsync("ActionThresholdsChanged")).ShouldBe(1);
        (await TextAsync("SELECT actor FROM accounts.account_events WHERE event = 'ActionThresholdsChanged'")).ShouldBe(ApiFactory.OwnerEmail);
    }

    [Fact]
    public async Task Invalid_values_are_a_400_with_a_field_error_each_and_nothing_is_stored()
    {
        using var response = await Client.PutJsonAsync("/api/portfolio/action-thresholds", Body(belowCost: 0m, high: 96m, positions: 1m, lagDays: 20m), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["belowCostPct", "belowCostHighPct", "minPositions", "lagDays"], ignoreOrder: true);
        (await CountAsync("SELECT count(*) FROM platform.app_settings WHERE key = 'portfolio.action_thresholds'")).ShouldBe(0);
        (await EventsAsync("ActionThresholdsChanged")).ShouldBe(0);
    }

    [Fact]
    public async Task A_missing_value_and_a_fraction_in_a_count_are_field_errors()
    {
        using var missing = await Client.PutJsonAsync("/api/portfolio/action-thresholds", new { belowCostPct = 10 }, Ct);
        using var fraction = await Client.PutJsonAsync("/api/portfolio/action-thresholds", Body(positions: 3.5m), Ct);

        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.ReadFieldErrorsAsync(Ct)).Keys.ShouldContain("gainPct");
        (await fraction.ReadFieldErrorsAsync(Ct)).ShouldContainKey("minPositions");
    }

    [Fact]
    public async Task Restore_defaults_writes_the_defaults_and_audits_it()
    {
        using var _ = await Client.PutJsonAsync("/api/portfolio/action-thresholds", Body(), Ct);

        using var response = await Client.PostAsync("/api/portfolio/action-thresholds/reset", Ct);
        var view = await response.ReadAsync<ActionThresholdsView>(Ct);

        view.IsDefault.ShouldBeTrue();
        (await GetAsync()).Values.ShouldBe(ActionThresholds.Defaults);
        (await EventsAsync("ActionThresholdsChanged")).ShouldBe(2);
        (await TextAsync("SELECT detail FROM accounts.account_events WHERE event = 'ActionThresholdsChanged' ORDER BY at DESC LIMIT 1")).ShouldBe("Action center thresholds restored to the defaults");
    }

    [Fact]
    public async Task A_stored_row_that_is_unreadable_or_out_of_range_falls_back_to_the_defaults()
    {
        await ExecuteAsync("INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('portfolio.action_thresholds', '{\"belowCostPct\":0}', 'someone', now())");

        var view = await GetAsync();
        await ExecuteAsync("UPDATE platform.app_settings SET value = 'not json' WHERE key = 'portfolio.action_thresholds'");
        var garbage = await GetAsync();

        (view.IsDefault, garbage.IsDefault).ShouldBe((true, true));
        (await ActionsAsync()).Counts.Open.ShouldBe(6);
    }

    [Fact]
    public async Task The_save_is_idempotent()
    {
        using var first = await Client.PutJsonAsync("/api/portfolio/action-thresholds", Body(), Ct);
        using var second = await Client.PutJsonAsync("/api/portfolio/action-thresholds", Body(), Ct);

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EventsAsync("ActionThresholdsChanged")).ShouldBe(1);
        (await second.Content.ReadFromJsonAsync<ActionThresholdsView>(Ct))!.Values.ShouldBe((await first.Content.ReadFromJsonAsync<ActionThresholdsView>(Ct))!.Values);
    }

    [Fact]
    public async Task Raising_a_threshold_so_a_done_item_vanishes_and_lowering_it_again_brings_the_item_back_open_with_no_read_between()
    {
        var below = (await ActionsAsync()).Items.Single(i => i is { Rule: "below_cost", Symbol: "AAA" });
        await DecidedAsync(below.Id, "done");

        using var raised = await Client.PutJsonAsync("/api/portfolio/action-thresholds", Body(belowCost: 35m, high: 50m), Ct);
        raised.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StateOfAsync("below_cost", await HoldingIdAsync("AAA"))).ShouldBe("cleared"); // cleared by the write itself
        using var reset = await Client.PostAsync("/api/portfolio/action-thresholds/reset", Ct);
        reset.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ActionsAsync()).Items.Single(i => i.Id == below.Id).State.ShouldBe("open");
    }
}
