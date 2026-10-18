using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>Scope 2 of P3-04: how the replay takes a strategy (the record of it) and the built-in factory (id and parameters to ORB).</summary>
public sealed class StrategyPreparationTests
{
    private static Dictionary<string, double> Params(params (string Name, double Value)[] values) => values.ToDictionary(v => v.Name, v => v.Value);

    [Fact]
    public void Prepare_gives_the_record_of_the_strategy_with_its_parameters_in_ordinal_name_order()
    {
        var build = StrategyPreparation.Prepare(OrbV1.Create(), Params(("rr", 2.0), ("or_min", 15)), Timeframe.M15);

        var record = build.Strategy.ShouldNotBeNull().Record;
        record.Id.ShouldBe("ORB");
        record.Parameters.Keys.ShouldBe(["or_min", "rr"]);
        record.EvaluationTimeframe.ShouldBe(Timeframe.M15);
        record.Sides.ShouldBe([Side.Buy, Side.Sell]);
        record.DefinitionSha256.Length.ShouldBe(64);
        build.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Prepare_refuses_what_the_simulator_refuses_and_the_located_problems_pass_through_unchanged()
    {
        var definition = TestStrategies.Swing(Timeframe.M15);
        var direct = Simulator.Create(definition, Timeframe.M15);

        var build = StrategyPreparation.Prepare(definition, Params(), Timeframe.M15);

        build.Strategy.ShouldBeNull();
        build.Problems.ShouldBe(direct.Problems);
        build.Problems.ShouldNotBeEmpty();
    }

    [Fact]
    public void Prepare_refuses_a_daily_evaluation_by_name()
    {
        var build = StrategyPreparation.Prepare(OrbV1.Create(), Params(), Timeframe.D1);

        build.Strategy.ShouldBeNull();
        build.Problems.ShouldContain(p => p.Problem.Code == SimulatorCodes.ScannerTimeframeUnsupported);
    }

    [Fact]
    public void The_fills_are_the_default_rules_when_none_are_given_and_the_given_rules_otherwise()
    {
        var byDefault = StrategyPreparation.Prepare(OrbV1.Create(), Params(), Timeframe.M15).Strategy.ShouldNotBeNull();
        var exact = StrategyPreparation.Prepare(OrbV1.Create(), Params(), Timeframe.M15, Cand.NoSlippage).Strategy.ShouldNotBeNull();

        byDefault.Fills.ShouldBe(FillRules.Default);
        exact.Fills.ShouldBe(Cand.NoSlippage);
        exact.Fills.ToCanonicalJson().ShouldNotBe(byDefault.Fills.ToCanonicalJson());
    }

    [Fact]
    public void The_parameters_are_copied_so_that_a_later_change_of_the_caller_does_not_move_the_record()
    {
        var given = new Dictionary<string, double> { ["or_min"] = 15 };
        var record = StrategyPreparation.Prepare(OrbV1.Create(), given, Timeframe.M15).Strategy.ShouldNotBeNull().Record;

        given["or_min"] = 30;
        given["extra"] = 1;

        record.Parameters.ShouldBe(new Dictionary<string, double> { ["or_min"] = 15 });
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_parameter_that_is_not_a_finite_number_is_a_caller_error(double value)
    {
        Should.Throw<ArgumentException>(() => StrategyPreparation.Prepare(OrbV1.Create(), Params(("or_min", value)), Timeframe.M15));
    }

    [Fact]
    public void A_prepared_strategy_can_be_shared_by_replays_that_run_at_the_same_time()
    {
        var prepared = Rep.Prepare(OrbV1.Create());
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.F1(ReplayDays.D2));
        var expected = RunText.Replay(Rep.Replay(prepared, bars, ReplayDays.D1, ReplayDays.D2));

        var results = Enumerable.Range(0, 8).AsParallel().Select(_ => RunText.Replay(Rep.Replay(prepared, bars, ReplayDays.D1, ReplayDays.D2))).ToList();

        results.ShouldAllBe(r => r == expected);
    }

    [Fact]
    public void The_built_in_factory_gives_orb_with_its_defaults_when_no_parameters_are_given()
    {
        var build = BuiltInStrategies.Build("ORB", null);

        build.Problems.ShouldBeEmpty();
        build.Definition.ShouldBe(OrbV1.Create(15, 2.0));
        build.Parameters.ShouldBe(new Dictionary<string, double> { ["or_min"] = 15, ["rr"] = 2.0 });
        build.Parameters.Keys.ShouldBe(["or_min", "rr"]);
    }

    [Fact]
    public void The_built_in_factory_takes_or_min_and_rr()
    {
        var build = BuiltInStrategies.Build("ORB", Params(("rr", 3.0), ("or_min", 30)));

        build.Definition.ShouldBe(OrbV1.Create(30, 3.0));
        build.Parameters.ShouldBe(new Dictionary<string, double> { ["or_min"] = 30, ["rr"] = 3.0 });
    }

    [Fact]
    public void The_built_in_factory_fills_in_the_default_of_a_parameter_that_is_not_given()
    {
        var build = BuiltInStrategies.Build("ORB", Params(("rr", 1.5)));

        build.Parameters.ShouldBe(new Dictionary<string, double> { ["or_min"] = 15, ["rr"] = 1.5 });
    }

    [Fact]
    public void An_unknown_strategy_id_is_a_located_problem_with_a_code()
    {
        var build = BuiltInStrategies.Build("orb", null);

        build.Definition.ShouldBeNull();
        var problem = build.Problems.ShouldHaveSingleItem();
        problem.Path.ShouldBe("strategy_id");
        problem.Problem.Code.ShouldBe(SimulationErrors.UnknownStrategyCode);
        problem.Problem.Message.ShouldContain("`ORB`");
    }

    [Fact]
    public void An_unknown_parameter_name_is_a_located_problem_for_each_name_in_ordinal_order()
    {
        var build = BuiltInStrategies.Build("ORB", Params(("zz", 1), ("or_min", 15), ("aa", 2)));

        build.Definition.ShouldBeNull();
        build.Parameters.ShouldBeEmpty();
        build.Problems.Select(p => (p.Path, p.Problem.Code)).ShouldBe([("parameters.aa", SimulationErrors.UnknownParameterCode), ("parameters.zz", SimulationErrors.UnknownParameterCode)]);
    }

    [Fact]
    public void The_built_in_ids_are_exactly_orb_and_a_prepared_factory_build_is_accepted_by_prepare()
    {
        BuiltInStrategies.Ids.ShouldBe(["ORB"]);
        var build = BuiltInStrategies.Build("ORB", Params(("or_min", 30)));

        var prepared = StrategyPreparation.Prepare(build.Definition!, build.Parameters, Timeframe.M5);

        prepared.Strategy.ShouldNotBeNull().Record.Parameters["or_min"].ShouldBe(30);
    }

    [Fact]
    public void The_factory_leaves_the_value_ranges_to_the_compile()
    {
        var build = BuiltInStrategies.Build("ORB", Params(("or_min", 7)));
        var prepared = StrategyPreparation.Prepare(build.Definition!, build.Parameters, Timeframe.M15);

        build.Problems.ShouldBeEmpty();
        prepared.Strategy.ShouldBeNull("an opening range of 7 minutes is not a multiple of the 15m bar: the compile refuses it, not the factory");
    }

    [Fact]
    public void The_factory_prepares_in_one_step_with_the_effective_parameters_and_the_problems_of_the_factory_or_the_compile()
    {
        var prepared = BuiltInStrategies.Prepare("ORB", Params(("or_min", 30)), Timeframe.M15);
        var unknown = BuiltInStrategies.Prepare("MACD", null, Timeframe.M15);
        var refused = BuiltInStrategies.Prepare("ORB", Params(("or_min", 7)), Timeframe.M15);

        prepared.Strategy.ShouldNotBeNull().Record.Parameters.ShouldBe(new Dictionary<string, double> { ["or_min"] = 30, ["rr"] = 2.0 });
        prepared.Strategy.Record.DefinitionSha256.ShouldBe(StrategyPreparation.Prepare(OrbV1.Create(30), prepared.Strategy.Record.Parameters, Timeframe.M15).Strategy!.Record.DefinitionSha256);
        unknown.Strategy.ShouldBeNull();
        unknown.Problems.Single().Problem.Code.ShouldBe(SimulationErrors.UnknownStrategyCode);
        refused.Strategy.ShouldBeNull();
        refused.Problems.ShouldNotBeEmpty();
        Should.Throw<ArgumentException>(() => BuiltInStrategies.Prepare("ORB", Params(("rr", double.NaN)), Timeframe.M15));
    }
}
