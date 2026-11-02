using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>
/// P15-09 criteria 1 and 3 (the model part): an event side, a side whose entry reads <c>days_since_results</c>, may hold 1 to 20 sessions and time out of a swing time stop up to 20; every other side keeps 1 to 5. The overlay <c>event_exit</c> compiles without the
/// warning <c>exit_not_evaluated</c>, the filter <c>not_event_day</c> beside an event side gets a warning, and the quintile features are refused on any evaluation timeframe but 1D (they are known as of the close of the date).
/// </summary>
public sealed class EventSideTests
{
    private const string HoldMessage = "1 to 5, or 1 to 20 for a side that reads days_since_results";

    private static ExitParameter P(string name, double value) => Defs.P(name, value);

    private static StrategyDefinition Swing(string trigger, int hold, params string[] filters) =>
        Defs.Swing().WithBuy(b => b with { Trigger = Defs.All(trigger) }) with { TimeRules = new TimeRules(null, null, null, hold), Filters = filters };

    private static StrategyDefinition WithOverlay(string trigger, int hold, OverlaySpec overlay) =>
        Defs.Swing().WithBuy(b => b with { Trigger = Defs.All(trigger), Overlays = [overlay] }) with { TimeRules = new TimeRules(null, null, null, hold) };

    private static OverlaySpec TimeStop(int sessions) => new(OverlayKind.TimeStop, [P("min_progress_rr", 0.5), P("swing_sessions", sessions)]);

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(20)]
    public void An_event_side_may_hold_one_to_twenty_sessions(int hold)
    {
        var result = Located.Compile(Swing("days_since_results == 0", hold), Timeframe.D1);

        result.IsSuccess.ShouldBeTrue(Located.Show(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void An_event_side_refuses_a_hold_outside_one_to_twenty(int hold)
    {
        var result = Located.Compile(Swing("days_since_results == 0", hold), Timeframe.D1);

        var found = result.ShouldHave(ModelCodes.MaxHoldOutOfRange, "time_rules.max_hold_sessions");
        found.Problem.Message.ShouldContain(HoldMessage);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(20)]
    public void Any_other_side_refuses_a_hold_over_five_with_the_message_that_names_the_event_side(int hold)
    {
        var result = Located.Compile(Swing("close > highest(20)", hold), Timeframe.D1);

        var found = result.ShouldHave(ModelCodes.MaxHoldOutOfRange, "time_rules.max_hold_sessions");
        found.Problem.Message.ShouldContain(HoldMessage);
    }

    [Fact]
    public void A_side_that_reads_days_to_results_only_is_not_an_event_side()
    {
        // only days_since_results marks a side that trades after results (decision 1 of the spec); days_to_results looks forward and does not
        var result = Located.Compile(Swing("days_to_results > 3", 6), Timeframe.D1);

        result.ShouldHave(ModelCodes.MaxHoldOutOfRange, "time_rules.max_hold_sessions");
    }

    [Fact]
    public void The_swing_time_stop_of_an_event_side_reaches_nineteen_sessions_under_a_hold_of_twenty()
    {
        var result = Located.Compile(WithOverlay("days_since_results == 0", 20, TimeStop(19)), Timeframe.D1);

        result.IsSuccess.ShouldBeTrue(Located.Show(result));
    }

    [Fact]
    public void The_swing_time_stop_of_an_event_side_must_still_be_below_its_hold()
    {
        var result = Located.Compile(WithOverlay("days_since_results == 0", 20, TimeStop(20)), Timeframe.D1);

        result.ShouldHave(ModelCodes.ValueOutOfRange, "buy.overlays[0].swing_sessions");
    }

    [Fact]
    public void The_swing_time_stop_of_any_other_side_is_still_one_to_five()
    {
        var result = Located.Compile(WithOverlay("close > highest(20)", 5, TimeStop(6)), Timeframe.D1);

        var found = result.ShouldHave(ModelCodes.ValueOutOfRange, "buy.overlays[0].swing_sessions");
        found.Problem.Message.ShouldContain("[1, 5]");
    }

    [Fact]
    public void The_event_exit_overlay_compiles_without_the_not_evaluated_warning_and_is_in_the_plan()
    {
        var overlay = new OverlaySpec(OverlayKind.EventExit, [P("days_before_results", 2)]);

        var result = Located.Compile(WithOverlay("days_since_results == 0", 20, overlay), Timeframe.D1);

        result.IsSuccess.ShouldBeTrue(Located.Show(result));
        result.Problems.ShouldNotContain(p => p.Problem.Code == ModelCodes.ExitNotEvaluated);
        result.Compiled!.Overlays.Select(o => o.Kind).ShouldBe([OverlayKind.EventExit]);
    }

    [Fact]
    public void The_filter_not_event_day_beside_an_event_side_is_a_warning_and_beside_another_side_is_not()
    {
        var eventSide = Located.Compile(Swing("days_since_results == 0", 20, FilterNames.NotEventDay), Timeframe.D1);
        var other = Located.Compile(Swing("close > highest(20)", 5, FilterNames.NotEventDay), Timeframe.D1);

        eventSide.IsSuccess.ShouldBeTrue(Located.Show(eventSide));
        eventSide.ShouldHave(ModelCodes.EventSideWithNotEventDay, "filters[0]", ProblemSeverity.Warning);
        other.Problems.ShouldNotContain(p => p.Problem.Code == ModelCodes.EventSideWithNotEventDay);
    }

    [Fact]
    public void The_quintile_features_are_read_on_the_daily_timeframe_only()
    {
        var daily = Located.Compile(Swing("days_since_results == 0 and results_sue_q == 5 and results_car3_q >= 2", 20), Timeframe.D1);
        var hourly = Located.Compile(Swing("results_car3_q == 5", 5), Timeframe.H1);

        daily.IsSuccess.ShouldBeTrue(Located.Show(daily));
        hourly.ShouldHave(ModelCodes.ClosingFeatureOffDaily, "buy.trigger[0]");
    }
}
