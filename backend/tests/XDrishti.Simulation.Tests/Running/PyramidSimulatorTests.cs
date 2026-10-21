using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criteria 5, 6 and 9 of P4-04 at the simulator: a strategy with a <c>PYRAMID</c> is accepted (its ladders are not candidate lists), alone and with the time stop, and refused by name when it has a second candidate or the <c>event_exit</c>
/// overlay; it runs end to end through the scanner, the tape and the walker over the F days (the entries are those of the <c>FIXED_RR</c> run, every trade has its legs, the counters of the adds add up); the full size reaches the walker through
/// the costed replay, so that an add of no whole share is skipped there and only there.
/// </summary>
public sealed class PyramidSimulatorTests
{
    private static ExitParameter P(string name, params double[] values) => new(name, [.. values.Select(Quantity.Of)]);

    private static ExitSpec Pyramid() => new(ExitPolicy.Pyramid, [P("initial_pct", 50), P("add_at_rr", 0.25, 0.5), P("add_pct", 30, 20), P("max_adds", 2), P("trail_atr_mult", 2), P("atr_period", 3)]);

    private static ExitSpec Rr() => new(ExitPolicy.FixedRr, [P("rr", 2)]);

    private static StrategyDefinition With(ExitSpec[] exits, OverlaySpec[]? overlays = null)
    {
        var orb = OrbV1.Create();
        return orb with { Buy = orb.Buy! with { Exits = exits, Overlays = overlays }, Sell = orb.Sell! with { Exits = exits, Overlays = overlays } };
    }

    private static string[] Errors(SimulatorBuild build) => [.. build.Problems.Where(p => p.Problem.Severity == ProblemSeverity.Error).Select(p => p.Problem.Code + "@" + p.Path)];

    public static TheoryData<string> Days => [.. Cases.FDays.Select(c => c.Name)];

    [Fact]
    public void A_pyramid_is_accepted_alone_and_with_the_time_stop_and_its_ladders_are_not_candidate_lists()
    {
        var alone = Simulator.Create(With([Pyramid()]), Timeframe.M15);
        var stop = Simulator.Create(With([Pyramid()], [new OverlaySpec(OverlayKind.TimeStop, [P("min_progress_rr", 0.5), P("intraday_bars", 4)])]), Timeframe.M15);

        alone.Simulator.ShouldNotBeNull();
        stop.Simulator.ShouldNotBeNull();
        Errors(alone).ShouldBeEmpty();
        Errors(stop).ShouldBeEmpty();
        alone.Problems.ShouldNotContain(p => p.Problem.Code == "strategy.model.exit_not_evaluated");
    }

    [Fact]
    public void A_second_candidate_and_the_event_exit_are_refused_by_name_at_their_paths()
    {
        var two = Simulator.Create(With([Rr(), Pyramid()]), Timeframe.M15);
        var overlay = Simulator.Create(With([Pyramid()], [new OverlaySpec(OverlayKind.EventExit, [P("days_before_results", 1)])]), Timeframe.M15);

        two.Simulator.ShouldBeNull();
        overlay.Simulator.ShouldBeNull();
        Errors(two).ShouldBe(["simulation.unsupported_exit@buy.exits", "simulation.unsupported_exit@sell.exits"], ignoreOrder: true);
        two.Problems.First(p => p.Problem.Code == "simulation.unsupported_exit").Problem.Message.ShouldContain("PYRAMID");
        Errors(overlay).ShouldBe(["simulation.unsupported_exit@buy.overlays[0]", "simulation.unsupported_exit@sell.overlays[0]"], ignoreOrder: true);
    }

    private static SimulationResult Run(StrategyDefinition definition, string day, Func<decimal, decimal, long>? quantity = null) =>
        Sim.Create(definition, fills: Cand.NoSlippage).Run(Cand.Test, Cases.F(day).Bars, Cand.Tick05, null, quantity).Value;

    [Theory]
    [MemberData(nameof(Days))]
    public void A_pyramid_walks_the_entries_of_the_FIXED_RR_run_and_every_trade_has_its_legs_and_its_counters_add_up(string day)
    {
        var reference = Run(With([Rr()]), day);
        var result = Run(With([Pyramid()]), day);

        result.Outcomes.Count.ShouldBe(reference.Outcomes.Count, day);
        for (var i = 0; i < reference.Outcomes.Count; i++)
        {
            if (result.Outcomes[i].Walk.Trade is { } trade && reference.Outcomes[i].Walk.Trade is { } plain)
            {
                (trade.EntryEpochSeconds, trade.EntryPrice, trade.StopPrice, trade.EntryFilledPrice).ShouldBe((plain.EntryEpochSeconds, plain.EntryPrice, plain.StopPrice, plain.EntryFilledPrice), day);
                trade.Legs.ShouldNotBeNull(day);
                trade.TargetPrice.ShouldBeNull(day);
            }
        }

        var c = result.Walk;
        c.Trades.ShouldBe(c.ExitsStop + c.ExitsSquareOff + c.ExitsMaxHold + c.ExitsEndOfData + c.ExitsTimeStop, day + ": a pyramid has no target");
        c.LegsTotal.ShouldBe(result.Outcomes.Sum(o => o.Walk.Trade?.Legs?.Count ?? 0), day);
        c.AddsTaken.ShouldBe(result.Outcomes.Sum(o => o.Walk.Trade?.Legs?.Count(l => l.Kind == LegKind.Add) ?? 0), day);
        c.AddsSkippedRisk.ShouldBe(0, day);
        c.AddsSkippedQuantity.ShouldBe(0, day + ": a gross run does not know the full size");
        reference.Walk.AddsTaken.ShouldBe(0);
        result.Header.EngineVersion.ShouldBe("sim-6");
    }

    [Fact]
    public void The_full_size_given_to_the_run_skips_the_adds_that_hold_no_whole_share()
    {
        var taken = 0L;
        var skipped = 0L;
        foreach (var day in Cases.FDays.Select(c => c.Name))
        {
            var known = Run(With([Pyramid()]), day, (_, _) => 2);
            taken += Run(With([Pyramid()]), day).Walk.AddsTaken;
            skipped += known.Walk.AddsSkippedQuantity;
            known.Walk.AddsTaken.ShouldBe(0, day + ": 0.3 x 2 and 0.2 x 2 are under one share");
        }

        taken.ShouldBeGreaterThan(0, "the F days reach the first level");
        skipped.ShouldBeGreaterThanOrEqualTo(taken, "every add taken without the size is skipped for its quantity with it");
    }

    [Fact]
    public void The_costed_replay_gives_the_walker_the_full_size_of_its_sizing()
    {
        // capital 1,000: Q = floor(min(1,000 x 0.5 % / 2.50, 1,000 x 25 % / 100.50)) = 2: the first leg holds 1 share, an add of 0.3 or 0.2 of 2 shares holds none
        var strategy = Rep.Prepare(With([Pyramid()]));
        var bars = ReplayDays.F1(ReplayDays.D1);

        var gross = Rep.Replay(strategy, bars, ReplayDays.D1, ReplayDays.D1);
        var costed = Rep.Replay(strategy, bars, ReplayDays.D1, ReplayDays.D1, costs: TestCostSheet.Context(1_000m, 0.5m));

        gross.Record.Walk.AddsTaken.ShouldBeGreaterThan(0);
        gross.Record.Walk.AddsSkippedQuantity.ShouldBe(0);
        costed.Record.Walk.AddsTaken.ShouldBe(0);
        costed.Record.Walk.AddsSkippedQuantity.ShouldBe(gross.Record.Walk.AddsTaken + gross.Record.Walk.AddsMissed);
    }
}
