using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criterion 12 of P3-03d: what <c>Simulator.Create</c> accepts and what stays refused now that the swing style is run. A swing strategy on 30m or 1h (BUY, <c>at_close</c>, an <c>or_opposite</c> stop read on the evaluation timeframe, one
/// <c>FIXED_RR</c>, <c>max_hold_sessions</c> 1 to 5) is accepted; everything else keeps the refusal of today by name and path, and a side that cannot be built always gives a problem.
/// </summary>
public sealed class SwingCreateTests
{
    private static StrategyDefinition Buy(Func<SideRules, SideRules> change) => TestStrategies.Swing1h().WithBuy(change);

    private static StrategyDefinition WithHold(int sessions) => TestStrategies.Swing1h(sessions);

    [Theory]
    [InlineData(Timeframe.M30, 1)]
    [InlineData(Timeframe.M30, 5)]
    [InlineData(Timeframe.H1, 1)]
    [InlineData(Timeframe.H1, 3)]
    [InlineData(Timeframe.H1, 5)]
    public void A_swing_strategy_on_30m_or_1h_with_a_hold_of_1_to_5_sessions_is_accepted(Timeframe timeframe, int sessions)
    {
        var definition = WithHold(sessions) with { Timeframes = [timeframe] };

        var build = Simulator.Create(definition, timeframe);

        build.Simulator.ShouldNotBeNull(string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        build.Simulator.HoldsOvernight.ShouldBeTrue();
        build.Simulator.Sides.ShouldBe([Side.Buy]);
    }

    [Theory]
    [InlineData(StopKind.SwingLow)]
    [InlineData(StopKind.BarLow)]
    public void The_low_level_stops_with_a_zero_buffer_on_the_evaluation_timeframe_are_accepted_for_a_swing_buy(StopKind kind)
    {
        var stop = kind is StopKind.SwingLow ? new StopSpec(kind, Lookback: Quantity.Of(5)) : new StopSpec(kind);

        var build = Simulator.Create(Buy(b => b with { Stop = stop }), Timeframe.H1);

        build.Simulator.ShouldNotBeNull(kind + ": " + string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
    }

    [Fact]
    public void A_daily_swing_strategy_is_accepted()
    {
        var build = Simulator.Create(TestStrategies.Swing(Timeframe.D1), Timeframe.D1);

        build.Simulator.ShouldNotBeNull(string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        build.Simulator.HoldsOvernight.ShouldBeTrue();
    }

    [Fact]
    public void An_intraday_strategy_does_not_hold_overnight()
    {
        Sim.Create(OrbV1.Create()).HoldsOvernight.ShouldBeFalse();
    }

    /// <summary>The refusals that stay: a definition, the code, the path of the field and a word the message names.</summary>
    public static TheoryData<string, StrategyDefinition, Timeframe, string, string> Refusals => new()
    {
        { "an overlay event_exit", Buy(b => b with { Overlays = [new OverlaySpec(OverlayKind.EventExit, [new ExitParameter("days_before_results", [Quantity.Of(1)])])] }), Timeframe.H1, "simulation.unsupported_exit", "buy.overlays[0]" },
        { "a filter", TestStrategies.Swing1h() with { Filters = ["min_turnover"] }, Timeframe.H1, "simulation.filter_not_available", "filters[0]" },
        { "a market context", Buy(b => b with { Trigger = TestStrategies.All("close > or_high(60) and vix < 20") }), Timeframe.H1, "simulation.needs_context", "buy" },
        { "an expiry read", Buy(b => b with { Trigger = TestStrategies.All("close > or_high(60) and is_expiry_day == 0") }), Timeframe.H1, "simulation.needs_expiry_days", "buy" },
        { "entry stop_above_high", Buy(b => b with { Entry = new EntrySpec(EntryKind.StopAboveHigh) }), Timeframe.H1, "simulation.unsupported_entry", "buy.entry" },
        { "entry limit_pullback", Buy(b => b with { Entry = new EntrySpec(EntryKind.LimitPullback) }), Timeframe.H1, "simulation.unsupported_entry", "buy.entry" },
        { "stop fixed_pct", Buy(b => b with { Stop = new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(2)) }), Timeframe.H1, "simulation.unsupported_stop", "buy.stop" },
        { "stop atr_mult", Buy(b => b with { Stop = new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2)) }), Timeframe.H1, "simulation.unsupported_stop", "buy.stop" },
        { "a buffer on the swing_low stop", Buy(b => b with { Stop = new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(5), BufferAtr: Quantity.Of(0.5)) }), Timeframe.H1, "simulation.unsupported_stop", "buy.stop.buffer_atr" },
        { "a bar_low stop read on another timeframe", Buy(b => b with { Stop = new StopSpec(StopKind.BarLow, Timeframe.M30) }), Timeframe.H1, "simulation.unsupported_stop", "buy.stop.timeframe" },
        { "a buffer on the or_opposite stop", Buy(b => b with { Stop = new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(60), BufferAtr: Quantity.Of(0.5)) }), Timeframe.H1, "simulation.unsupported_stop", "buy.stop.buffer_atr" },
        { "an or_opposite stop read on another timeframe", Buy(b => b with { Stop = new StopSpec(StopKind.OrOpposite, Timeframe.M30, Minutes: Quantity.Of(60)) }), Timeframe.H1, "simulation.unsupported_stop", "buy.stop.timeframe" },
        { "two reward-to-risk values", Buy(b => b with { Exits = [new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [Quantity.Of(2), Quantity.Of(3)])])] }), Timeframe.H1, "simulation.unsupported_exit", "buy.exits" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void What_stays_refused_is_refused_by_name_with_the_path_and_no_simulator(string label, StrategyDefinition definition, Timeframe timeframe, string code, string path)
    {
        var build = Simulator.Create(definition, timeframe);

        build.Simulator.ShouldBeNull(label);
        build.Problems.ShouldContain(p => p.Problem.Code == code && p.Path == path, label + ": " + string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        build.Problems.ShouldNotContain(p => p.Problem.Code == "simulation.unsupported_style", label);
    }

    [Theory]
    [InlineData(Timeframe.W1)]
    [InlineData(Timeframe.Mo1)]
    public void A_weekly_or_monthly_evaluation_is_refused_with_a_problem_and_no_simulator(Timeframe timeframe)
    {
        var build = Simulator.Create(TestStrategies.Swing(timeframe), timeframe);

        build.Simulator.ShouldBeNull();
        build.Problems.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_swing_sell_is_refused_by_the_compile_and_the_refusal_reaches_the_caller()
    {
        var definition = TestStrategies.Swing1h() with { Sides = [Side.Buy, Side.Sell], Sell = TestStrategies.Swing1h().Buy };

        var build = Simulator.Create(definition, Timeframe.H1);

        build.Simulator.ShouldBeNull();
        build.Problems.ShouldContain(p => p.Problem.Code == "strategy.model.swing_sell_unsupported" && p.Path == "sides[1]");
    }

    [Fact]
    public void A_hold_of_zero_or_six_sessions_or_none_is_refused_by_the_compile_and_gives_no_simulator()
    {
        foreach (var sessions in new[] { 0, 6 })
        {
            var build = Simulator.Create(WithHold(sessions), Timeframe.H1);
            build.Simulator.ShouldBeNull("hold " + sessions);
            build.Problems.ShouldContain(p => p.Path == "time_rules.max_hold_sessions", "hold " + sessions);
        }

        var none = Simulator.Create(TestStrategies.Swing1h() with { TimeRules = null }, Timeframe.H1);
        none.Simulator.ShouldBeNull();
        none.Problems.ShouldNotBeEmpty();
    }

    [Fact]
    public void No_simulator_without_a_problem_over_a_grid_of_styles_timeframes_and_time_rules()
    {
        // The path of the first draft in which a side with a null square-off yielded no simulator and no problem is closed: whatever is built, a missing simulator always has an error to show.
        var checkedCount = 0;
        var timeRules = new TimeRules?[]
        {
            null,
            new TimeRules(null, null, null, null),
            new TimeRules(null, null, new TimeOnly(15, 15), null),
            new TimeRules(null, null, null, 3),
            new TimeRules(new TimeOnly(10, 15), new TimeOnly(14, 15), null, 5),
            new TimeRules(new TimeOnly(10, 15), new TimeOnly(14, 15), new TimeOnly(15, 15), 5),
            new TimeRules(null, null, null, 0),
            new TimeRules(null, null, null, 6),
        };
        foreach (var style in new[] { Style.Intraday, Style.Swing })
        {
            foreach (var timeframe in new[] { Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1 })
            {
                foreach (var rules in timeRules)
                {
                    var definition = TestStrategies.Swing1h() with { Style = style, Timeframes = [timeframe], TimeRules = rules };

                    var build = Simulator.Create(definition, timeframe);

                    if (build.Simulator is null)
                    {
                        build.Problems.ShouldContain(p => p.Problem.Severity == ProblemSeverity.Error, $"{style} {timeframe} {rules}");
                    }

                    checkedCount++;
                }
            }
        }

        checkedCount.ShouldBe(2 * 5 * 8);
    }

    [Fact]
    public void The_code_of_the_retired_style_refusal_is_no_longer_declared()
    {
        typeof(SimulatorCodes).GetFields().Select(f => f.Name).ShouldNotContain("UnsupportedStyle");
    }

    [Fact]
    public void A_swing_run_with_a_calendar_that_has_no_trading_day_in_30_days_fails_with_rules_invalid_and_not_a_loop()
    {
        var simulator = Sim.Create(WithHold(3), Timeframe.H1);
        var bars = FixtureR.Bars(TestCalendars.Fri);

        var result = simulator.Run(Scan.Instrument, bars, Sim.Tick, TestCalendars.NoTradingDays(TestCalendars.Mon));

        // The calendar says Tuesday to Friday and every weekday up to 40 days are holidays; the entry day is Monday, which is a session. Only a hold of 2 or more searches.
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.rules_invalid");
    }
}
