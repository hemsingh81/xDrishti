using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>Criteria 1 to 4 of P3-06: <c>BASE_BREAKOUT</c> is exactly the definition of D1, compiles on 1D without a warning, sits in the registry as the third row, and its four ranges are enforced before the compile.</summary>
public sealed class BaseBreakoutDefinitionTests
{
    private static readonly string[] Setup =
    [
        "tf(\"1W\").close > tf(\"1W\").sma(10)",
        "close > sma(50)",
        "(highest(base_len, source=close) - lowest(base_len, source=close)) / lowest(base_len, source=close) * 100 <= base_pct",
        "rel_volume(20) >= vol_x",
    ];

    private static FeatureSpec Spec(string name, Timeframe timeframe, PriceSource source = PriceSource.Close, double p1 = 0) => new(name, timeframe, source, p1);

    private static Dictionary<string, double> Params(params (string Name, double Value)[] values) => values.ToDictionary(v => v.Name, v => v.Value);

    private static StrategyDefinition Literal(double baseLen = 20, double basePct = 8, double volX = 1.5, double rr = 2.0) => new(
        StrategyDefinition.CurrentSchema,
        "BASE_BREAKOUT",
        Style.Swing,
        [Timeframe.D1],
        [Side.Buy],
        [new StrategyParam("base_len", baseLen), new StrategyParam("base_pct", basePct), new StrategyParam("vol_x", volX)],
        new SideRules(
            new ConditionList(ListMode.All, Setup),
            new ConditionList(ListMode.All, ["close >= highest(base_len, source=close)"]),
            null,
            new EntrySpec(EntryKind.AtClose),
            new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("base_len")),
            [new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [Quantity.Of(rr)])])],
            null),
        null,
        new TimeRules(null, null, null, 5),
        []);

    [Fact]
    public void The_definition_is_exactly_the_one_of_the_spec_and_has_no_sell_block_and_no_filter()
    {
        BaseBreakoutV1.Create().ShouldBe(Literal());
        BaseBreakoutV1.Create().Sell.ShouldBeNull();
        BaseBreakoutV1.Create().Filters.ShouldBeEmpty();
    }

    [Fact]
    public void Its_canonical_text_equals_the_golden_file_checked_in_with_the_tests()
    {
        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "base-breakout-v1.definition.golden.txt")).TrimEnd('\r', '\n');

        Rep.Prepare(BaseBreakoutV1.Create(), Timeframe.D1).Record.DefinitionJson.ShouldBe(golden);
    }

    [Fact]
    public void A_different_argument_changes_exactly_the_one_value_it_names()
    {
        BaseBreakoutV1.Create(baseLen: 30).ShouldBe(Literal(baseLen: 30));
        BaseBreakoutV1.Create(basePct: 5).ShouldBe(Literal(basePct: 5));
        BaseBreakoutV1.Create(volX: 2).ShouldBe(Literal(volX: 2));
        BaseBreakoutV1.Create(rewardRisk: 3).ShouldBe(Literal(rr: 3));
        BaseBreakoutV1.Create().ShouldBe(BaseBreakoutV1.Create());
    }

    [Fact]
    public void The_defaults_compile_on_1D_with_no_error_and_no_warning_through_the_compiler_the_simulator_and_the_preparation()
    {
        var compiled = StrategyCompiler.Compile(BaseBreakoutV1.Create(), Timeframe.D1, Side.Buy);
        var simulator = Simulator.Create(BaseBreakoutV1.Create(), Timeframe.D1);
        var prepared = StrategyPreparation.Prepare(BaseBreakoutV1.Create(), new Dictionary<string, double>(), Timeframe.D1);

        compiled.IsSuccess.ShouldBeTrue(string.Join("; ", compiled.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        compiled.Problems.ShouldBeEmpty();
        simulator.Problems.ShouldBeEmpty();
        simulator.Simulator.ShouldNotBeNull().HoldsOvernight.ShouldBeTrue();
        prepared.Problems.ShouldBeEmpty();
        prepared.Strategy.ShouldNotBeNull();
        compiled.Compiled!.NeedsContext.ShouldBeFalse();
        compiled.Compiled.NeedsExpiryDays.ShouldBeFalse();
    }

    [Fact]
    public void The_compiled_feature_set_is_these_and_no_others()
    {
        var buy = Scan.Compile(BaseBreakoutV1.Create(), Side.Buy, Timeframe.D1);

        buy.Features.ShouldBe(
        [
            Spec("close", Timeframe.W1), Spec("sma", Timeframe.W1, p1: 10), Spec("close", Timeframe.D1), Spec("sma", Timeframe.D1, p1: 50),
            Spec("highest", Timeframe.D1, PriceSource.Close, 20), Spec("lowest", Timeframe.D1, PriceSource.Close, 20), Spec("rel_volume", Timeframe.D1, p1: 20), Spec("lowest", Timeframe.D1, PriceSource.Low, 20),
        ]);
    }

    [Theory]
    [InlineData(Timeframe.M5)]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.H1)]
    public void An_intraday_timeframe_is_refused_by_the_model_as_not_declared(Timeframe timeframe)
    {
        var prepared = BuiltInStrategies.Prepare("BASE_BREAKOUT", null, timeframe);

        prepared.Strategy.ShouldBeNull();
        prepared.Problems.ShouldContain(p => p.Problem.Code == "strategy.model.timeframe_not_declared");
    }

    [Fact]
    public void The_weekly_timeframe_is_refused_by_the_simulator_even_when_the_definition_declares_it()
    {
        var weekly = BaseBreakoutV1.Create() with { Timeframes = [Timeframe.W1] };

        var build = Simulator.Create(weekly, Timeframe.W1);

        build.Simulator.ShouldBeNull();
        build.Problems.ShouldContain(p => p.Problem.Code == ScannerCodes.TimeframeUnsupported);
        BuiltInStrategies.Prepare("BASE_BREAKOUT", null, Timeframe.W1).Strategy.ShouldBeNull();
    }

    [Fact]
    public void A_sell_block_added_by_hand_is_refused_for_a_swing_strategy()
    {
        var both = BaseBreakoutV1.Create() with { Sides = [Side.Buy, Side.Sell], Sell = BaseBreakoutV1.Create().Buy };

        var build = Simulator.Create(both, Timeframe.D1);

        build.Simulator.ShouldBeNull();
        build.Problems.ShouldContain(p => p.Problem.Code == ModelCodes.SwingSellUnsupported);
    }

    [Fact]
    public void The_catalog_row_the_ids_and_the_effective_parameters_of_a_default_build_are_the_four_values()
    {
        var prepared = BuiltInStrategies.Prepare("BASE_BREAKOUT", null, Timeframe.D1);

        prepared.Problems.ShouldBeEmpty();
        prepared.Strategy.ShouldNotBeNull().Record.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ShouldBe([("base_len", 20.0), ("base_pct", 8.0), ("rr", 2.0), ("vol_x", 1.5)]);
    }

    [Fact]
    public void An_unknown_name_is_refused_and_the_message_lists_the_four_parameters()
    {
        var build = BuiltInStrategies.Build("BASE_BREAKOUT", Params(("bogus", 1)));

        build.Problems.Single().Problem.Code.ShouldBe(SimulationErrors.UnknownParameterCode);
        build.Problems.Single().Problem.Message.ShouldContain("its parameters are `base_len`, `base_pct`, `vol_x` and `rr`");
    }

    [Theory]
    [InlineData("base_len", 10.0)]
    [InlineData("base_len", 60.0)]
    [InlineData("base_pct", 2.0)]
    [InlineData("base_pct", 20.0)]
    [InlineData("vol_x", 1.0)]
    [InlineData("vol_x", 5.0)]
    [InlineData("rr", 10.0)]
    [InlineData("rr", 0.01)]
    public void A_value_at_either_end_of_its_range_is_accepted(string name, double value)
    {
        var build = BuiltInStrategies.Build("BASE_BREAKOUT", Params((name, value)));

        build.Problems.ShouldBeEmpty();
        build.Definition.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("base_len", 9.99)]
    [InlineData("base_len", 60.01)]
    [InlineData("base_pct", 1.99)]
    [InlineData("base_pct", 20.01)]
    [InlineData("vol_x", 0.99)]
    [InlineData("vol_x", 5.01)]
    [InlineData("rr", 0.0)]
    [InlineData("rr", -1.0)]
    [InlineData("rr", 10.5)]
    public void A_value_just_outside_its_range_is_one_located_problem_and_no_definition(string name, double value)
    {
        var build = BuiltInStrategies.Build("BASE_BREAKOUT", Params((name, value)));

        build.Definition.ShouldBeNull();
        var problem = build.Problems.Single();
        problem.Path.ShouldBe("parameters." + name);
        problem.Problem.Code.ShouldBe("simulation.parameter_out_of_range");
    }

    [Fact]
    public void A_fractional_base_length_inside_the_range_is_refused_by_the_compile_as_an_invalid_feature()
    {
        var prepared = BuiltInStrategies.Prepare("BASE_BREAKOUT", Params(("base_len", 20.5)), Timeframe.D1);

        prepared.Strategy.ShouldBeNull();
        prepared.Problems.ShouldContain(p => p.Problem.Code == ModelCodes.FeatureInvalid);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_value_is_not_judged_by_the_range_and_prepare_keeps_throwing_its_argument_exception(double value)
    {
        BuiltInStrategies.Build("BASE_BREAKOUT", Params(("vol_x", value))).Problems.ShouldBeEmpty();
        Should.Throw<ArgumentException>(() => BuiltInStrategies.Prepare("BASE_BREAKOUT", Params(("vol_x", value)), Timeframe.D1));
    }

    [Fact]
    public void The_ranges_of_the_other_strategies_are_unaffected()
    {
        BuiltInStrategies.Build("ORB", Params(("or_min", -5))).Problems.ShouldBeEmpty();
        BuiltInStrategies.Build("VWAP_PULLBACK", Params(("pull_atr", 3.0))).Problems.ShouldBeEmpty();
    }
}
