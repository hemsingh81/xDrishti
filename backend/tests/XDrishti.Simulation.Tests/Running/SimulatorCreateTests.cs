using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criteria 11 and 12 of P3-03a: what <c>Simulator.Create</c> refuses by name (with the path of the field, the span of a model problem and a message that names the kind), that a half strategy is never run, that the problems of the
/// compile are passed on unchanged and its warnings carried, and that <c>Run</c> checks the order of the minutes once and fails with its own code.
/// </summary>
public sealed class SimulatorCreateTests
{
    private static ExitSpec Rr(params double[] values) => new(ExitPolicy.FixedRr, [new ExitParameter("rr", [.. values.Select(Quantity.Of)])]);

    private static StrategyDefinition Buy(Func<SideRules, SideRules> change) => OrbV1.Create().WithBuy(change);

    private static StrategyDefinition Sell(Func<SideRules, SideRules> change) => OrbV1.Create() with { Sell = change(OrbV1.Create().Sell!) };

    private static StrategyDefinition WithTrigger(string buyTrigger) => Buy(b => b with { Trigger = TestStrategies.All(buyTrigger) });

    /// <summary>The refusals of criterion 11: a definition, the code, the path and a word the message must contain (the offending kind).</summary>
    public static TheoryData<string, StrategyDefinition, string, string, string> Refusals => new()
    {
        { "entry stop_above_high", Buy(b => b with { Entry = new EntrySpec(EntryKind.StopAboveHigh) }), "simulation.unsupported_entry", "buy.entry", "stop_above_high" },
        { "entry limit_pullback on sell", Sell(s => s with { Entry = new EntrySpec(EntryKind.LimitPullback) }), "simulation.unsupported_entry", "sell.entry", "limit_pullback" },
        { "stop fixed_pct", Buy(b => b with { Stop = new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(1)) }), "simulation.unsupported_stop", "buy.stop", "fixed_pct" },
        { "stop atr_mult", Buy(b => b with { Stop = new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2)) }), "simulation.unsupported_stop", "buy.stop", "atr_mult" },
        { "stop swing_low with a buffer", Buy(b => b with { Stop = new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(3), BufferAtr: Quantity.Of(0.5)) }), "simulation.unsupported_stop", "buy.stop.buffer_atr", "swing_low" },
        { "stop bar_high on sell on another timeframe", Sell(s => s with { Stop = new StopSpec(StopKind.BarHigh, Timeframe.M5) }), "simulation.unsupported_stop", "sell.stop.timeframe", "bar_high" },
        { "stop or_opposite with a buffer", Buy(b => b with { Stop = new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(15), BufferAtr: Quantity.Of(0.5)) }), "simulation.unsupported_stop", "buy.stop.buffer_atr", "buffer_atr" },
        { "stop or_opposite on another timeframe", Buy(b => b with { Stop = new StopSpec(StopKind.OrOpposite, Timeframe.M5, Minutes: Quantity.Of(15)) }), "simulation.unsupported_stop", "buy.stop.timeframe", "5m" },
        { "two exit policies", Buy(b => b with { Exits = [Rr(2), new ExitSpec(ExitPolicy.AtrTrail, [new ExitParameter("mult", [Quantity.Of(2)]), new ExitParameter("activate_at_rr", [Quantity.Of(1)])])] }), "simulation.unsupported_exit", "buy.exits", "FIXED_RR" },
        { "an exit the simulator does not run", Buy(b => b with { Exits = [new ExitSpec(ExitPolicy.Pyramid, [new ExitParameter("initial_pct", [Quantity.Of(50)]), new ExitParameter("add_at_rr", [Quantity.Of(1), Quantity.Of(2)]), new ExitParameter("add_pct", [Quantity.Of(30), Quantity.Of(20)]), new ExitParameter("max_adds", [Quantity.Of(2)]), new ExitParameter("trail_atr_mult", [Quantity.Of(2)])])] }), "simulation.unsupported_exit", "buy.exits", "FIXED_RR" },
        { "several rr values", Sell(s => s with { Exits = [Rr(1.5, 2, 3)] }), "simulation.unsupported_exit", "sell.exits", "rr" },
        { "an overlay event_exit", Buy(b => b with { Overlays = [new OverlaySpec(OverlayKind.EventExit, [new ExitParameter("days_before_results", [Quantity.Of(1)])])] }), "simulation.unsupported_exit", "buy.overlays[0]", "event_exit" },
        { "a filter", OrbV1.Create() with { Filters = ["min_turnover"] }, "simulation.filter_not_available", "filters[0]", "min_turnover" },
        { "a market context", WithTrigger("close > or_high(or_min) and close[1] <= or_high(or_min) and vix < 20"), "simulation.needs_context", "buy", "market context" },
        { "expiry dates", WithTrigger("close > or_high(or_min) and close[1] <= or_high(or_min) and is_expiry_day == 0"), "simulation.needs_expiry_days", "buy", "expiry" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void What_the_simulator_cannot_run_is_refused_by_name_with_the_path_of_the_field(string label, StrategyDefinition definition, string code, string path, string word)
    {
        var timeframe = definition.Timeframes[^1];

        var build = Simulator.Create(definition, timeframe);

        build.Simulator.ShouldBeNull(label);
        var refusal = build.Problems.Where(p => p.Problem.Code == code).ToList();
        refusal.ShouldNotBeEmpty(label + ": " + string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        refusal.ShouldContain(p => p.Path == path, label);
        var problem = refusal.First(p => p.Path == path).Problem;
        problem.Span.ShouldBe(new SourceSpan(0, 0, 1, 1), label);
        problem.Severity.ShouldBe(ProblemSeverity.Error, label);
        problem.Message.ShouldContain(word, Case.Insensitive, label);
    }

    [Theory]
    [InlineData(StopKind.SwingHigh)]
    [InlineData(StopKind.BarHigh)]
    public void The_high_level_stops_with_a_zero_buffer_on_the_evaluation_timeframe_are_accepted_for_a_sell(StopKind kind)
    {
        var stop = kind is StopKind.SwingHigh ? new StopSpec(kind, Lookback: Quantity.Of(5)) : new StopSpec(kind);

        var build = Simulator.Create(Sell(s => s with { Stop = stop }), Timeframe.M15);

        build.Simulator.ShouldNotBeNull(kind + ": " + string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
    }

    [Fact]
    public void A_filter_is_refused_at_its_own_index_and_one_problem_per_filter_is_given()
    {
        var definition = OrbV1.Create() with { Filters = ["not_event_day", "min_turnover"] };

        var build = Simulator.Create(definition, Timeframe.M15);

        build.Problems.Where(p => p.Problem.Code == "simulation.filter_not_available").Select(p => p.Path).ShouldBe(["filters[0]", "filters[1]"]);
    }

    [Fact]
    public void A_daily_evaluation_is_accepted_and_the_four_level_stops_run_beside_or_opposite()
    {
        var build = Simulator.Create(TestStrategies.Swing(Timeframe.D1), Timeframe.D1);

        build.Simulator.ShouldNotBeNull(string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        build.Simulator.HoldsOvernight.ShouldBeTrue();
        build.Problems.ShouldNotContain(p => p.Problem.Code == "simulation.scanner_timeframe_unsupported");
    }

    [Fact]
    public void Both_sides_are_checked_and_both_are_reported()
    {
        var definition = Buy(b => b with { Entry = new EntrySpec(EntryKind.StopAboveHigh) }) with { Sell = OrbV1.Create().Sell! with { Entry = new EntrySpec(EntryKind.LimitPullback) } };

        var build = Simulator.Create(definition, Timeframe.M15);

        build.Problems.Where(p => p.Problem.Code == "simulation.unsupported_entry").Select(p => p.Path).ShouldBe(["buy.entry", "sell.entry"]);
    }

    [Fact]
    public void The_supported_definition_gives_a_simulator_and_no_problem()
    {
        var build = Simulator.Create(OrbV1.Create(), Timeframe.M15);

        build.Simulator.ShouldNotBeNull();
        build.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void The_problems_of_the_compile_are_passed_on_unchanged_and_a_half_strategy_is_never_run()
    {
        var definition = OrbV1.Create() with { Sell = OrbV1.Create().Sell! with { Trigger = TestStrategies.All("close > or_hgh(or_min)") } };
        var compiled = StrategyCompiler.Compile(definition, Timeframe.M15, Side.Sell);

        var build = Simulator.Create(definition, Timeframe.M15);

        compiled.IsSuccess.ShouldBeFalse();
        build.Simulator.ShouldBeNull("the BUY side compiles, the SELL side does not: nothing is run");
        build.Problems.ShouldBe(compiled.Problems);
        build.Problems.Single().Path.ShouldBe("sell.trigger[0]");
    }

    [Fact]
    public void The_warnings_of_the_compile_are_carried_when_a_simulator_exists()
    {
        var definition = OrbV1.Create() with { Params = [new StrategyParam("or_min", 15), new StrategyParam("volume", 1)] };

        var build = Simulator.Create(definition, Timeframe.M15);

        build.Simulator.ShouldNotBeNull();
        build.Problems.ShouldNotBeEmpty();
        build.Problems.ShouldAllBe(p => p.Problem.Severity == ProblemSeverity.Warning);
        build.Problems.ShouldContain(p => p.Problem.Code == "strategy.model.param_shadows_name" && p.Path == "params[1]");
        build.Problems.Count.ShouldBe(build.Problems.Distinct().Count(), "a problem both sides found is given once");
    }

    [Fact]
    public void A_definition_that_lists_no_side_gives_no_simulator_and_the_compile_says_why()
    {
        var build = Simulator.Create(OrbV1.Create() with { Sides = [] }, Timeframe.M15);

        build.Simulator.ShouldBeNull();
        build.Problems.ShouldContain(p => p.Problem.Code == "strategy.model.sides_empty");
    }

    [Fact]
    public void An_evaluation_timeframe_that_is_no_timeframe_is_a_programming_error_like_in_the_compiler()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Simulator.Create(OrbV1.Create(), (Timeframe)99));
    }

    // criterion 12

    [Fact]
    public void Two_equal_epochs_fail_the_run_before_any_step()
    {
        var bars = Fixtures.F1().Bars();

        var result = Sim.Create(OrbV1.Create()).Run(Scan.Instrument, [bars[0], bars[1], bars[1], bars[2]], Sim.Tick);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.bars_not_ascending");
    }

    [Fact]
    public void A_descending_pair_in_a_long_list_fails_the_run()
    {
        var bars = Fixtures.F1().Bars();
        (bars[200], bars[201]) = (bars[201], bars[200]);

        var result = Sim.Create(OrbV1.Create()).Run(Scan.Instrument, bars, Sim.Tick);

        result.Error.Code.ShouldBe("simulation.bars_not_ascending");
    }

    [Fact]
    public void An_ascending_list_is_not_refused()
    {
        Sim.Create(OrbV1.Create()).Run(Scan.Instrument, Fixtures.F1().Bars(), Sim.Tick).IsSuccess.ShouldBeTrue();
    }
}
