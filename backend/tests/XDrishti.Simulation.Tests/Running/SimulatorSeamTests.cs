using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criterion 13 of P3-03a (the walker-dependent part): the scanner knows nothing of exits, so the candidates of F1 to F13 are equal field by field, traces included, whatever the reward-to-risk of the strategy, and the trades differ
/// as the exit rule says. (The part about trailing exits runs on the scanner in <c>ScannerSeamTests</c>; <c>Simulator.Create</c> refuses them by name.)
/// </summary>
public sealed class SimulatorSeamTests
{
    private static readonly double[] Rewards = [1.5, 2.0, 3.0];

    private static readonly string[] Days = ["F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "F13"];

    private static SimulationResult Run(double rewardRisk, Bar[] bars) => Sim.Run(OrbV1.Create(rewardRisk: rewardRisk), bars);

    private static string[] Candidates(SimulationResult result) => [.. result.Outcomes.Select(o => Render.Candidate(o.Candidate))];

    [Fact]
    public void The_candidates_of_f1_to_f13_with_their_traces_are_equal_for_a_reward_risk_of_1_5_2_and_3()
    {
        foreach (var name in Days)
        {
            var bars = Fixtures.OrbDays.Single(f => f.Name == name).Bars;

            var reference = Candidates(Run(2.0, bars));

            Candidates(Run(1.5, bars)).ShouldBe(reference, name);
            Candidates(Run(3.0, bars)).ShouldBe(reference, name);
        }
    }

    [Fact]
    public void The_scanner_counters_are_equal_whatever_the_reward_risk()
    {
        foreach (var name in Days)
        {
            var bars = Fixtures.OrbDays.Single(f => f.Name == name).Bars;

            Run(1.5, bars).Scans.ShouldBe(Run(3.0, bars).Scans, name);
        }
    }

    [Fact]
    public void F1_hits_a_target_of_104_25_at_10_30_with_1_5_and_105_50_at_11_10_with_2_0()
    {
        var bars = Fixtures.OrbDays.Single(f => f.Name == "F1").Bars;

        var one = Run(1.5, bars).Trades.Single();
        var two = Run(2.0, bars).Trades.Single();

        (one.TargetPrice, one.ExitReason, one.ExitEpochSeconds, one.R).ShouldBe((104.25, ExitReason.Target, Day.Epoch(Day.Monday, Day.Minute("10:30")), 1.5));
        (two.TargetPrice, two.ExitReason, two.ExitEpochSeconds, two.R).ShouldBe((105.50, ExitReason.Target, Day.Epoch(Day.Monday, Day.Minute("11:10")), 2.0));
    }

    [Fact]
    public void F1_with_3_0_never_reaches_108_and_runs_to_the_square_off()
    {
        var bars = Fixtures.OrbDays.Single(f => f.Name == "F1").Bars;

        var three = Run(3.0, bars).Trades.Single();

        (three.TargetPrice, three.ExitReason, three.ExitPrice, three.ExitEpochSeconds).ShouldBe((108.00, ExitReason.SquareOff, 105.00, Day.Epoch(Day.Monday, Day.Minute("15:15"))));
        three.R.ShouldBe(1.8);
    }

    [Fact]
    public void The_trades_of_the_three_rewards_differ_on_the_days_where_the_target_matters()
    {
        var bars = Fixtures.OrbDays.Single(f => f.Name == "F1").Bars;

        var rendered = Rewards.Select(rr => Render.Result(Run(rr, bars))).Distinct().ToList();

        rendered.Count.ShouldBe(3);
    }

    [Fact]
    public void A_simulator_refuses_a_trailing_exit_by_name_instead_of_running_the_same_entries_under_it()
    {
        var trail = OrbV1.Create().WithBuy(b => b with { Exits = [new ExitSpec(ExitPolicy.AtrTrail, [new ExitParameter("atr_mult", [Quantity.Of(2)])])] });

        Simulator.Create(trail, Timeframe.M15).Problems.ShouldContain(p => p.Problem.Code == "simulation.unsupported_exit");
    }
}
