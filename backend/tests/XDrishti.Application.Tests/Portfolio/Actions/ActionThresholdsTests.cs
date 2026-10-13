using XDrishti.Application.Portfolio.Actions;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>The thresholds of the rules (acceptance criteria 7 and 12): the documented defaults, validated ranges with field errors, Restore defaults, the audit event, and no change recorded when nothing changes.</summary>
public sealed class ActionThresholdsTests
{
    private static ActionThresholdsInput Input(
        decimal? belowCost = 12m, decimal? high = 22m, decimal? gain = 30m, decimal? floor = 20m, decimal? multiple = 2m, decimal? positions = 4m, decimal? concentration = 40m, decimal? lag = 12m, decimal? lagDays = 200m, decimal? window = 45m) =>
        new(belowCost, high, gain, floor, multiple, positions, concentration, lag, lagDays, window);

    private static async Task<Result<ActionThresholdsView>> SaveAsync(ActionsHarness harness, ActionThresholdsInput input) =>
        await new SaveActionThresholdsHandler(harness.Writer()).HandleAsync(new SaveActionThresholdsCommand(input, ActionsHarness.Owner), CancellationToken.None);

    private static string[] Fields(Result<ActionThresholdsView> result) => [.. result.Error.Fields!.Select(f => f.Field)];

    [Fact]
    public async Task Without_stored_values_the_documented_defaults_apply()
    {
        var harness = new ActionsHarness();

        var view = await new GetActionThresholdsHandler(harness.Data).HandleAsync(new GetActionThresholdsQuery(), CancellationToken.None);

        view.Values.ShouldBe(new ActionThresholds(10m, 20m, 25m, 15m, 1.5m, 3, 35m, 10m, 180, 30));
        (view.Defaults, view.IsDefault, view.UpdatedBy, view.UpdatedAt).ShouldBe((view.Values, true, null, null));
    }

    [Fact]
    public async Task Saving_stores_the_values_audits_the_change_and_the_next_read_returns_them()
    {
        var harness = new ActionsHarness();

        var saved = await SaveAsync(harness, Input());

        saved.IsSuccess.ShouldBeTrue();
        saved.Value.Values.ShouldBe(new ActionThresholds(12m, 22m, 30m, 20m, 2m, 4, 40m, 12m, 200, 45));
        (saved.Value.IsDefault, saved.Value.UpdatedBy).ShouldBe((false, ActionsHarness.Owner));
        harness.Data.Thresholds.BelowCostPct.ShouldBe(12m);
        var audit = harness.Data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Actor, audit.AccountId).ShouldBe((AccountEventType.ActionThresholdsChanged, ActionsHarness.Owner, null));
        audit.Detail.ShouldBe("Action center thresholds changed: belowCostPct 10 to 12, belowCostHighPct 20 to 22, gainPct 25 to 30, overweightFloorPct 15 to 20, overweightMultiple 1.5 to 2, minPositions 3 to 4, concentrationPct 35 to 40, lagPp 10 to 12, lagDays 180 to 200, longTermWindowDays 30 to 45");
    }

    [Fact]
    public async Task Saving_the_values_already_in_force_changes_and_records_nothing()
    {
        var harness = new ActionsHarness();
        await SaveAsync(harness, Input());
        var events = harness.Data.Events.Count;

        var again = await SaveAsync(harness, Input());
        var defaults = await SaveAsync(harness, Input(10m, 20m, 25m, 15m, 1.5m, 3m, 35m, 10m, 180m, 30m));

        again.IsSuccess.ShouldBeTrue();
        defaults.Value.IsDefault.ShouldBeTrue();
        harness.Data.Events.Count.ShouldBe(events + 1); // only the change back to the defaults
    }

    [Fact]
    public async Task Restore_defaults_writes_the_defaults_and_audits_it()
    {
        var harness = new ActionsHarness();
        await SaveAsync(harness, Input());

        var reset = await new ResetActionThresholdsHandler(harness.Writer()).HandleAsync(new ResetActionThresholdsCommand(ActionsHarness.Owner), CancellationToken.None);

        reset.Value.IsDefault.ShouldBeTrue();
        harness.Data.Thresholds.ShouldBe(ActionThresholds.Defaults);
        harness.Data.Events.Last().Detail.ShouldBe("Action center thresholds restored to the defaults");
    }

    [Fact]
    public async Task Restore_defaults_when_they_are_in_force_records_nothing()
    {
        var harness = new ActionsHarness();

        var reset = await new ResetActionThresholdsHandler(harness.Writer()).HandleAsync(new ResetActionThresholdsCommand(ActionsHarness.Owner), CancellationToken.None);

        reset.IsSuccess.ShouldBeTrue();
        harness.Data.Events.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0.99, "belowCostPct")]
    [InlineData(90.01, "belowCostPct")]
    [InlineData(-5, "belowCostPct")]
    public async Task The_below_cost_threshold_is_one_to_ninety(double value, string field)
    {
        var result = await SaveAsync(new ActionsHarness(), Input(belowCost: (decimal)value));

        Fields(result).ShouldContain(field);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(90, true)]
    [InlineData(0.5, false)]
    public async Task The_edges_of_the_below_cost_range_are_in(double value, bool accepted)
    {
        var result = await SaveAsync(new ActionsHarness(), Input(belowCost: (decimal)value, high: 95m));

        result.IsSuccess.ShouldBe(accepted);
    }

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public async Task A_value_outside_its_range_is_refused_with_a_field_error_and_nothing_is_stored(ActionThresholdsInput input, string field)
    {
        var harness = new ActionsHarness();

        var result = await SaveAsync(harness, input);

        result.Error.Code.ShouldBe("portfolio.invalid");
        Fields(result).ShouldBe([field]);
        harness.Data.Thresholds.ShouldBe(ActionThresholds.Defaults);
        harness.Data.Events.ShouldBeEmpty();
    }

    public static TheoryData<ActionThresholdsInput, string> OutOfRange => new()
    {
        { Input(high: 1.9m, belowCost: 1m), "belowCostHighPct" },
        { Input(high: 95.1m), "belowCostHighPct" },
        { Input(gain: 0.9m), "gainPct" },
        { Input(gain: 1000.1m), "gainPct" },
        { Input(floor: 0.9m), "overweightFloorPct" },
        { Input(floor: 100.1m), "overweightFloorPct" },
        { Input(multiple: 0.9m), "overweightMultiple" },
        { Input(multiple: 10.1m), "overweightMultiple" },
        { Input(positions: 1m), "minPositions" },
        { Input(positions: 51m), "minPositions" },
        { Input(concentration: 4.9m), "concentrationPct" },
        { Input(concentration: 100.1m), "concentrationPct" },
        { Input(lag: 0.9m), "lagPp" },
        { Input(lag: 100.1m), "lagPp" },
        { Input(lagDays: 29m), "lagDays" },
        { Input(lagDays: 3651m), "lagDays" },
        { Input(window: 0m), "longTermWindowDays" },
        { Input(window: 366m), "longTermWindowDays" },
    };

    [Theory]
    [InlineData(10, 10)]
    [InlineData(25, 20)]
    public async Task The_high_band_must_be_above_the_below_cost_band(double belowCost, double high)
    {
        var result = await SaveAsync(new ActionsHarness(), Input(belowCost: (decimal)belowCost, high: (decimal)high));

        Fields(result).ShouldBe(["belowCostHighPct"]);
        result.Error.Fields!.Single().Message.ShouldBe("Must be greater than the below-cost threshold.");
    }

    [Fact]
    public async Task The_range_edges_are_accepted()
    {
        var result = await SaveAsync(new ActionsHarness(), Input(belowCost: 1m, high: 2m, gain: 1000m, floor: 100m, multiple: 10m, positions: 50m, concentration: 100m, lag: 100m, lagDays: 3650m, window: 365m));

        result.IsSuccess.ShouldBeTrue();
        var low = await SaveAsync(new ActionsHarness(), Input(belowCost: 1m, high: 2m, gain: 1m, floor: 1m, multiple: 1m, positions: 2m, concentration: 5m, lag: 1m, lagDays: 30m, window: 1m));
        low.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Every_value_is_required_and_all_problems_come_back_at_once()
    {
        var result = await SaveAsync(new ActionsHarness(), new ActionThresholdsInput(null, null, 0m, null, null, null, null, null, null, null));

        Fields(result).ShouldBe(["belowCostPct", "belowCostHighPct", "overweightFloorPct", "overweightMultiple", "minPositions", "concentrationPct", "lagPp", "lagDays", "longTermWindowDays"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("minPositions")]
    [InlineData("lagDays")]
    [InlineData("longTermWindowDays")]
    public async Task The_counts_must_be_whole_numbers(string field)
    {
        var input = field switch
        {
            "minPositions" => Input(positions: 3.5m),
            "lagDays" => Input(lagDays: 180.5m),
            _ => Input(window: 30.5m),
        };

        var result = await SaveAsync(new ActionsHarness(), input);

        Fields(result).ShouldBe([field]);
        result.Error.Fields!.Single().Message.ShouldBe("Use a whole number.");
    }

    [Fact]
    public async Task A_percentage_has_at_most_two_decimals()
    {
        var result = await SaveAsync(new ActionsHarness(), Input(gain: 25.123m));

        Fields(result).ShouldBe(["gainPct"]);
    }

    [Fact]
    public async Task A_missing_body_is_refused()
    {
        var result = await new SaveActionThresholdsHandler(new ActionsHarness().Writer()).HandleAsync(new SaveActionThresholdsCommand(null!, ActionsHarness.Owner), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task A_save_that_collides_is_a_conflict()
    {
        var harness = new ActionsHarness();
        harness.Data.RefuseNextSave = true;

        var result = await SaveAsync(harness, Input());

        result.Error.Code.ShouldBe("portfolio.conflict");
        harness.Data.Thresholds.ShouldBe(ActionThresholds.Defaults);
    }
}
