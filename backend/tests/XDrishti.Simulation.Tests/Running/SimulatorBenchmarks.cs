using System.Diagnostics;
using System.Globalization;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criterion 17c of P3-03a (the walk added to the scan): the explicit benchmark, not part of <c>scripts/check.sh</c>. Replays 250 seeded sessions (93,750 minutes) through the scan of both sides and through <c>Simulator.Run</c>
/// (scan and walk), and walks the candidates of the run alone; prints minutes per second for each and the share of a run spent walking. Reported and not asserted; run with <c>--explicit only</c>.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class SimulatorBenchmarks
{
    private static double MinutesPerSecond(int minutes, Action run)
    {
        run();
        var clock = Stopwatch.StartNew();
        run();
        return minutes / clock.Elapsed.TotalSeconds;
    }

    [Fact(Explicit = true)]
    public void Report_minutes_per_second_for_the_scan_of_both_sides_the_whole_run_and_the_walk()
    {
        var bars = BenchmarkSessions.Minutes();
        var definition = OrbV1.Create();
        var buy = Scan.Compile(definition, Side.Buy);
        var sell = Scan.Compile(definition, Side.Sell);
        var simulator = Sim.Create(definition);
        var candidates = Sim.Run(simulator, bars).Outcomes.Select(o => o.Candidate).ToList();
        var rules = new WalkRules(2.0, 915, FillRules.Default, Sim.Tick);

        var scan = MinutesPerSecond(bars.Length, () =>
        {
            Scan.Run(buy, bars);
            Scan.Run(sell, bars);
        });
        var run = MinutesPerSecond(bars.Length, () => Sim.Run(simulator, bars));
        var walk = MinutesPerSecond(bars.Length, () =>
        {
            foreach (var candidate in candidates)
            {
                TradeWalker.Walk(candidate, bars, rules);
            }
        });

        TestContext.Current.TestOutputHelper?.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0:N0} minutes, {1} candidates; scan of both sides {2:N0} min/s; whole run {3:N0} min/s; the {1} walks alone take {4:F2} ms; share of a run spent walking {5:P1}; 214 instruments x 190,000 minutes at the run rate: {6:N0} s",
            bars.Length,
            candidates.Count,
            scan,
            run,
            bars.Length / walk * 1000,
            1 - run / scan > 0 ? 1 - run / scan : 0,
            214 * 190_000 / run));
    }
}
