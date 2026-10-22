using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Running;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>The candidates of the tests of P4-05: ORB v1 with the exits of the flags of <see cref="ExitFlags"/>, built the way the engine builds them (15m, no slippage).</summary>
internal static class Compared
{
    /// <summary>One candidate of each of the seven policies, with parameters that make the walk do something on the seeded series.</summary>
    public static readonly string[] Seven =
    [
        "FIXED_RR:rr=2",
        "PARTIAL_BE:book_pct=50,t1_rr=1,t2_rr=2",
        "PARTIAL_TRAIL:book_pct=50,t1_rr=1,trail_atr_mult=2,atr_period=3",
        "ATR_TRAIL:mult=2,activate_at_rr=0.5,atr_period=5",
        "STRUCTURE_TRAIL:lookback_bars=3",
        "EMA_TRAIL:length=5",
        "PYRAMID:initial_pct=50,add_at_rr=0.25|0.5,add_pct=30|20,max_adds=2,trail_atr_mult=2,atr_period=3",
    ];

    /// <summary>The choice of the flags; the test fails with the usage errors when they are refused.</summary>
    public static ExitChoice Choice(string? timeStop, params string[] exits)
    {
        var errors = new List<string>();
        var choice = ExitFlags.Parse(exits, timeStop, errors);
        choice.ShouldNotBeNull(string.Join("; ", errors));
        return choice;
    }

    /// <summary>ORB v1 whose exits are the given candidates (all of them, for the union) and whose overlay is the choice's.</summary>
    public static StrategyDefinition Orb(ExitChoice choice, IReadOnlyList<ExitCandidate> candidates) => ExitChoices.With(OrbV1.Create(), candidates, choice);

    /// <summary>The comparison of all the candidates of the choice.</summary>
    public static SimulatorComparison Comparison(ExitChoice choice)
    {
        var members = choice.Candidates.Select(c => Orb(choice, [c])).ToList();
        var build = SimulatorComparison.Create(members, Timeframe.M15, Cand.NoSlippage);
        build.Comparison.ShouldNotBeNull(string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        return build.Comparison;
    }

    /// <summary>The simulator of one candidate alone, as <see cref="Simulator.Create"/> builds a plain run.</summary>
    public static Simulator Alone(ExitChoice choice, int index) => Sim.Create(Orb(choice, [choice.Candidates[index]]), Timeframe.M15, Cand.NoSlippage);
}
