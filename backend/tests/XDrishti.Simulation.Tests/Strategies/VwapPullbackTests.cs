using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>Criteria 1 and 2 of P3-05: <c>VWAP_PULLBACK</c> is exactly the definition of D1, compiles on 15m without a warning, and its parameter range is enforced by the registry before the compile.</summary>
public sealed class VwapPullbackTests
{
    private static FeatureSpec Spec(string name, Timeframe timeframe, PriceSource source = PriceSource.Close, double p1 = 0) => new(name, timeframe, source, p1);

    private static SideRules Side(string[] setup, string[] trigger, StopKind stop) => new(
        new ConditionList(ListMode.All, setup),
        new ConditionList(ListMode.All, trigger),
        null,
        new EntrySpec(EntryKind.AtClose),
        new StopSpec(stop, Lookback: Quantity.Of(2)),
        [new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [Quantity.Of(2.0)])])],
        null);

    private static Dictionary<string, double> Params(params (string Name, double Value)[] values) => values.ToDictionary(v => v.Name, v => v.Value);

    [Fact]
    public void The_definition_is_exactly_the_one_of_the_spec()
    {
        var literal = new StrategyDefinition(
            StrategyDefinition.CurrentSchema,
            "VWAP_PULLBACK",
            Style.Intraday,
            [Timeframe.M15],
            [Domain.Research.Side.Buy, Domain.Research.Side.Sell],
            [new StrategyParam("pull_atr", 0.5)],
            Side(
                ["tf(\"1h\").ema(20) > tf(\"1h\").ema(50)", "tf(\"1D\").close > tf(\"1D\").ema(20)"],
                ["close > vwap and close > open", "close[1] > vwap[1] and close[2] > vwap[2]", "low[1] < low[2] and low[1] <= vwap[1] + pull_atr * atr(14)"],
                StopKind.SwingLow),
            Side(
                ["tf(\"1h\").ema(20) < tf(\"1h\").ema(50)", "tf(\"1D\").close < tf(\"1D\").ema(20)"],
                ["close < vwap and close < open", "close[1] < vwap[1] and close[2] < vwap[2]", "high[1] > high[2] and high[1] >= vwap[1] - pull_atr * atr(14)"],
                StopKind.SwingHigh),
            new TimeRules(new TimeOnly(10, 0), new TimeOnly(14, 30), new TimeOnly(15, 15), null),
            []);

        VwapPullback.Create().ShouldBe(literal);
        VwapPullback.Create().Filters.ShouldBeEmpty();
    }

    [Fact]
    public void Each_call_builds_an_equal_definition_and_both_parameters_reach_the_definition()
    {
        var custom = VwapPullback.Create(pullAtr: 1.0, rewardRisk: 1.5);

        VwapPullback.Create().ShouldBe(VwapPullback.Create());
        custom.Params.ShouldBe([new StrategyParam("pull_atr", 1.0)]);
        custom.Buy!.Exits!.Single().Parameters.Single().Values.ShouldBe([Quantity.Of(1.5)]);
        custom.Sell!.Exits!.Single().Parameters.Single().Values.ShouldBe([Quantity.Of(1.5)]);
    }

    [Theory]
    [InlineData(Domain.Research.Side.Buy)]
    [InlineData(Domain.Research.Side.Sell)]
    public void Both_sides_compile_on_15m_with_no_error_and_no_warning_and_need_no_context(Side side)
    {
        var result = StrategyCompiler.Compile(VwapPullback.Create(), Timeframe.M15, side);

        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        result.Problems.ShouldBeEmpty();
        result.Compiled!.HistoryDepth.ShouldBe(3);
        result.Compiled.NeedsContext.ShouldBeFalse();
        result.Compiled.NeedsExpiryDays.ShouldBeFalse();
    }

    [Fact]
    public void The_compiled_feature_lists_are_pinned_buy_and_sell()
    {
        var buy = Scan.Compile(VwapPullback.Create(), Domain.Research.Side.Buy);
        var sell = Scan.Compile(VwapPullback.Create(), Domain.Research.Side.Sell);

        buy.Features.ShouldBe(
        [
            Spec("ema", Timeframe.H1, p1: 20), Spec("ema", Timeframe.H1, p1: 50), Spec("close", Timeframe.D1), Spec("ema", Timeframe.D1, p1: 20),
            Spec("close", Timeframe.M15), Spec("vwap", Timeframe.M15), Spec("open", Timeframe.M15), Spec("low", Timeframe.M15), Spec("atr", Timeframe.M15, p1: 14), Spec("lowest", Timeframe.M15, PriceSource.Low, 2),
        ]);
        sell.Features.ShouldBe(
        [
            Spec("ema", Timeframe.H1, p1: 20), Spec("ema", Timeframe.H1, p1: 50), Spec("close", Timeframe.D1), Spec("ema", Timeframe.D1, p1: 20),
            Spec("close", Timeframe.M15), Spec("vwap", Timeframe.M15), Spec("open", Timeframe.M15), Spec("high", Timeframe.M15), Spec("atr", Timeframe.M15, p1: 14), Spec("highest", Timeframe.M15, PriceSource.High, 2),
        ]);
    }

    [Theory]
    [InlineData(Timeframe.M5)]
    [InlineData(Timeframe.M30)]
    public void The_strategy_is_not_offered_on_another_timeframe_than_15m(Timeframe timeframe)
    {
        var entry = BuiltInStrategies.Catalog.Single(c => c.Id == "VWAP_PULLBACK");
        var prepared = BuiltInStrategies.Prepare("VWAP_PULLBACK", null, timeframe);

        entry.Timeframes.ShouldBe([Timeframe.M15]);
        prepared.Strategy.ShouldBeNull();
        prepared.Problems.ShouldContain(p => p.Problem.Code == "strategy.model.timeframe_not_declared" && p.Problem.Message.Contains(timeframe.ToCode(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(3.01)]
    public void A_pull_outside_the_declared_range_is_one_located_problem_and_no_definition(double value)
    {
        var build = BuiltInStrategies.Build("VWAP_PULLBACK", Params(("pull_atr", value)));

        build.Definition.ShouldBeNull();
        var problem = build.Problems.Single();
        problem.Path.ShouldBe("parameters.pull_atr");
        problem.Problem.Code.ShouldBe(SimulationErrors.ParameterOutOfRangeCode);
        problem.Problem.Code.ShouldBe("simulation.parameter_out_of_range");
        problem.Problem.Message.ShouldContain("`0 < x <= 3`");
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(3.0)]
    [InlineData(1e-9)]
    public void A_pull_inside_the_declared_range_is_accepted(double value)
    {
        var build = BuiltInStrategies.Build("VWAP_PULLBACK", Params(("pull_atr", value)));

        build.Problems.ShouldBeEmpty();
        build.Definition.ShouldNotBeNull().Params.ShouldBe([new StrategyParam("pull_atr", value)]);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_pull_is_not_judged_by_the_range_and_prepare_keeps_throwing_its_argument_exception(double value)
    {
        var build = BuiltInStrategies.Build("VWAP_PULLBACK", Params(("pull_atr", value)));

        build.Problems.ShouldBeEmpty();
        build.Definition.ShouldNotBeNull();
        Should.Throw<ArgumentException>(() => BuiltInStrategies.Prepare("VWAP_PULLBACK", Params(("pull_atr", value)), Timeframe.M15));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(10.01)]
    public void A_reward_risk_outside_the_exit_range_is_refused_by_the_compile_at_the_exit_and_not_by_the_registry(double value)
    {
        BuiltInStrategies.Build("VWAP_PULLBACK", Params(("rr", value))).Problems.ShouldBeEmpty();

        var prepared = BuiltInStrategies.Prepare("VWAP_PULLBACK", Params(("rr", value)), Timeframe.M15);

        prepared.Strategy.ShouldBeNull();
        prepared.Problems.Select(p => (p.Path, p.Problem.Code)).ShouldContain(("buy.exits[0].rr[0]", "strategy.model.value_out_of_range"));
    }

    [Fact]
    public void An_unknown_name_is_refused_and_the_message_lists_both_parameters()
    {
        var build = BuiltInStrategies.Build("VWAP_PULLBACK", Params(("bogus", 1)));

        build.Problems.Single().Problem.Message.ShouldContain("its parameters are `pull_atr` and `rr`");
        build.Problems.Single().Problem.Code.ShouldBe(SimulationErrors.UnknownParameterCode);
    }

    [Fact]
    public void An_unknown_name_is_refused_before_the_range_is_looked_at()
    {
        var build = BuiltInStrategies.Build("VWAP_PULLBACK", Params(("bogus", 1), ("pull_atr", -5)));

        build.Problems.Select(p => p.Problem.Code).ShouldBe([SimulationErrors.UnknownParameterCode]);
    }

    [Fact]
    public void The_effective_parameters_recorded_are_the_defaults_when_none_are_given()
    {
        var prepared = BuiltInStrategies.Prepare("VWAP_PULLBACK", null, Timeframe.M15);

        prepared.Problems.ShouldBeEmpty();
        prepared.Strategy.ShouldNotBeNull().Record.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ShouldBe([("pull_atr", 0.5), ("rr", 2.0)]);
    }

    [Fact]
    public void Orb_declares_no_range_and_keeps_its_messages_and_a_parameter_info_with_three_arguments_has_no_bounds()
    {
        var orb = BuiltInStrategies.Catalog.Single(c => c.Id == "ORB");
        var pull = BuiltInStrategies.Catalog.Single(c => c.Id == "VWAP_PULLBACK").Parameters.Single(p => p.Name == "pull_atr");
        var plain = new BuiltInParameterInfo("x", 1, "note");

        orb.Parameters.ShouldAllBe(p => !p.HasRange && p.Min == null && p.Max == null && !p.MinExclusive);
        (pull.Min, pull.Max, pull.MinExclusive).ShouldBe((0.0, 3.0, true));
        (plain.Min, plain.Max, plain.MinExclusive).ShouldBe((null, null, false));
        BuiltInStrategies.Build("ORB", Params(("or_min", -5))).Problems.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, 3.0, false, "x <= 3")]
    [InlineData(0.0, null, true, "0 < x")]
    [InlineData(1.0, 5.0, false, "1 <= x <= 5")]
    [InlineData(0.0, 3.0, true, "0 < x <= 3")]
    public void The_range_text_has_one_side_when_one_bound_is_declared(double? min, double? max, bool exclusive, string expected)
    {
        new BuiltInParameterInfo("x", 1, "n", min, max, exclusive).RangeText().ShouldBe(expected);
    }
}
