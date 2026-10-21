using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Spec P3-02b2, criterion 13 and D2/D14: the model may describe more than the simulator can run. A correct table of any policy or overlay compiles and <c>Simulator.Create</c> refuses it by name
/// (<c>simulation.unsupported_exit</c>); a bad table is the compile's own problem; and the set of policies the compile warns about is exactly the complement of what the simulator runs.
/// </summary>
public sealed class ExitBoundaryTests
{
    private static ExitParameter P(string name, params double[] values) => new(name, [.. values.Select(Quantity.Of)]);

    private static ExitSpec Rr(double rr) => new(ExitPolicy.FixedRr, [P("rr", rr)]);

    private static ExitParameter[] Valid(ExitPolicy policy) => policy switch
    {
        ExitPolicy.FixedRr => [P("rr", 2)],
        ExitPolicy.PartialBe => [P("book_pct", 50), P("t1_rr", 1), P("t2_rr", 2)],
        ExitPolicy.PartialTrail => [P("book_pct", 50), P("t1_rr", 1), P("trail_atr_mult", 2)],
        ExitPolicy.AtrTrail => [P("mult", 2), P("activate_at_rr", 1)],
        ExitPolicy.StructureTrail => [P("lookback_bars", 3)],
        ExitPolicy.EmaTrail => [P("length", 20)],
        _ => [P("initial_pct", 50), P("add_at_rr", 1, 2), P("add_pct", 30, 20), P("max_adds", 2), P("trail_atr_mult", 2)],
    };

    private static StrategyDefinition With(ExitSpec[] exits, OverlaySpec[]? overlays = null) => OrbV1.Create().WithBuy(b => b with { Exits = exits, Overlays = overlays });

    private static string[] Errors(SimulatorBuild build) => [.. build.Problems.Where(p => p.Problem.Severity == ProblemSeverity.Error).Select(p => p.Problem.Code + "@" + p.Path)];

    public static TheoryData<ExitPolicy> Refused { get; } = [ExitPolicy.Pyramid];

    public static TheoryData<ExitPolicy> Run { get; } = [ExitPolicy.FixedRr, ExitPolicy.PartialBe, ExitPolicy.PartialTrail, ExitPolicy.AtrTrail, ExitPolicy.StructureTrail, ExitPolicy.EmaTrail];

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_correct_table_of_a_policy_the_simulator_does_not_run_is_refused_by_name_alone_and_with_a_FIXED_RR(ExitPolicy policy)
    {
        var alone = Simulator.Create(With([new ExitSpec(policy, Valid(policy))]), Timeframe.M15);
        var withRr = Simulator.Create(With([Rr(2), new ExitSpec(policy, Valid(policy))]), Timeframe.M15);

        alone.Simulator.ShouldBeNull();
        Errors(alone).ShouldBe(["simulation.unsupported_exit@buy.exits"]);
        alone.Problems.Single(p => p.Problem.Code == "simulation.unsupported_exit").Problem.Message.ShouldContain(policy switch { ExitPolicy.AtrTrail => "ATR_TRAIL", ExitPolicy.StructureTrail => "STRUCTURE_TRAIL", ExitPolicy.EmaTrail => "EMA_TRAIL", _ => "PYRAMID" });
        Errors(withRr).ShouldBe(["simulation.unsupported_exit@buy.exits"]);
    }

    [Theory]
    [MemberData(nameof(Run))]
    public void A_correct_table_of_a_policy_the_simulator_runs_is_accepted_alone_and_with_the_time_stop(ExitPolicy policy)
    {
        var alone = Simulator.Create(With([new ExitSpec(policy, Valid(policy))]), Timeframe.M15);
        var overlay = Simulator.Create(With([new ExitSpec(policy, Valid(policy))], [new OverlaySpec(OverlayKind.TimeStop, [P("min_progress_rr", 0.5), P("intraday_bars", 12)])]), Timeframe.M15);

        alone.Simulator.ShouldNotBeNull();
        overlay.Simulator.ShouldNotBeNull();
        Errors(alone).ShouldBeEmpty();
        Errors(overlay).ShouldBeEmpty();
    }

    [Fact]
    public void Two_candidates_are_refused_at_the_exits_and_a_list_of_two_values_is_refused_naming_the_policy_and_the_parameter()
    {
        var two = Simulator.Create(With([Rr(2), new ExitSpec(ExitPolicy.PartialBe, Valid(ExitPolicy.PartialBe))]), Timeframe.M15);
        var rrList = Simulator.Create(With([new ExitSpec(ExitPolicy.FixedRr, [P("rr", 2, 3)])]), Timeframe.M15);
        var t2List = Simulator.Create(With([new ExitSpec(ExitPolicy.PartialBe, [P("book_pct", 50), P("t1_rr", 1), P("t2_rr", 2, 3)])]), Timeframe.M15);

        Errors(two).ShouldBe(["simulation.unsupported_exit@buy.exits"]);
        Errors(rrList).ShouldBe(["simulation.unsupported_exit@buy.exits"]);
        Errors(t2List).ShouldBe(["simulation.unsupported_exit@buy.exits"]);
        rrList.Problems.Single(p => p.Problem.Code == "simulation.unsupported_exit").Problem.Message.ShouldContain("FIXED_RR");
        rrList.Problems.Single(p => p.Problem.Code == "simulation.unsupported_exit").Problem.Message.ShouldContain("`rr`");
        t2List.Problems.Single(p => p.Problem.Code == "simulation.unsupported_exit").Problem.Message.ShouldContain("PARTIAL_BE");
        t2List.Problems.Single(p => p.Problem.Code == "simulation.unsupported_exit").Problem.Message.ShouldContain("`t2_rr`");
    }

    [Fact]
    public void The_time_stop_is_accepted_and_event_exit_is_refused_by_name_at_its_overlay()
    {
        var overlays = new[]
        {
            new OverlaySpec(OverlayKind.TimeStop, [P("min_progress_rr", 0.5), P("intraday_bars", 12)]),
            new OverlaySpec(OverlayKind.EventExit, [P("days_before_results", 1)]),
        };

        var build = Simulator.Create(With([Rr(2)], overlays), Timeframe.M15);

        build.Simulator.ShouldBeNull();
        Errors(build).ShouldBe(["simulation.unsupported_exit@buy.overlays[1]"]);
    }

    [Fact]
    public void A_bad_table_is_the_compiles_own_problem_and_not_unsupported_exit()
    {
        var bad = With([new ExitSpec(ExitPolicy.PartialBe, [P("book_pct", 50), P("t1_rr", 2), P("t2_rr", 1)])]);

        var build = Simulator.Create(bad, Timeframe.M15);

        build.Simulator.ShouldBeNull();
        Errors(build).ShouldBe(["strategy.model.value_out_of_range@buy.exits[0].t2_rr[0]"]);
    }

    [Fact]
    public void The_policies_the_simulator_runs_are_exactly_the_ones_the_compile_does_not_warn_about()
    {
        foreach (var policy in Enum.GetValues<ExitPolicy>())
        {
            var definition = With([new ExitSpec(policy, Valid(policy))]);
            var compiled = StrategyCompiler.Compile(definition, Timeframe.M15, XDrishti.Domain.Research.Side.Buy);
            var warned = compiled.Problems.Any(p => p.Problem.Code == ModelCodes.ExitNotEvaluated);
            var run = Simulator.Create(definition, Timeframe.M15).Simulator is not null;

            warned.ShouldBe(!run, policy.ToString());
        }
    }
}
