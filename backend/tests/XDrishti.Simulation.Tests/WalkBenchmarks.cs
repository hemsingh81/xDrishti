using System.Diagnostics;
using System.Globalization;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// AC12: an explicit benchmark, not part of <c>scripts/check.sh</c>: 250 seeded sessions (93,750 minutes) with 3 candidates per side; reports walks and minutes per second, asserts nothing.
/// Run in Release: <c>dotnet test --project tests/XDrishti.Simulation.Tests -c Release -- --explicit only --filter-method "*Walk_throughput*"</c>.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class WalkBenchmarks
{
    private const int Sessions = 250;

    [Fact(Explicit = true)]
    public void Walk_throughput_over_250_sessions_is_reported()
    {
        var bars = new List<Domain.Market.Bars.Bar>();
        var random = new Random(2026);
        var price = 100.0;
        for (var s = 0; s < Sessions; s++)
        {
            var date = DayBuilder.Monday.AddDays(s / 5 * 7 + s % 5);
            for (var minute = Domain.Market.Bars.SessionClock.OpenMinute; minute < Domain.Market.Bars.SessionClock.CloseMinute; minute++)
            {
                var open = price;
                price *= 1 + ((random.NextDouble() - 0.5) * 0.001);
                bars.Add(new Domain.Market.Bars.Bar(DayBuilder.Epoch(date, minute), open, Math.Max(open, price) * 1.0002, Math.Min(open, price) * 0.9998, price, 1_000));
            }
        }

        var walks = new List<Simulation.EntryCandidate>();
        for (var s = 0; s < Sessions; s++)
        {
            foreach (var side in new[] { Domain.Research.Side.Buy, Domain.Research.Side.Sell })
            {
                for (var c = 0; c < 3; c++)
                {
                    var index = (s * 375) + 30 + (c * 60);
                    var close = bars[index].Close;
                    var stop = side == Domain.Research.Side.Buy ? close * 0.995 : close * 1.005;
                    walks.Add(new Simulation.EntryCandidate("ORB", Cand.Test, side, index, bars[index].EpochSeconds + 60, Domain.Market.Bars.Timeframe.M15, bars[index], 15, true, stop, Simulation.DecisionTrace.Empty));
                }
            }
        }

        // The exact layer (the rules of the compatibility with P3-03) and the modelled one (the doc 16 slippage on the tick grid): the same walks, so that the price of the decimal arithmetic and the slippage is visible.
        foreach (var (name, rules) in new[] { ("exact", Cand.Standard), ("modelled", Cand.Modelled) })
        {
            var watch = Stopwatch.StartNew();
            long minutes = 0;
            foreach (var candidate in walks)
            {
                var outcome = TradeWalker.Walk(candidate, bars, rules).Value;
                minutes += outcome.BarsExamined;
            }

            watch.Stop();
            var seconds = watch.Elapsed.TotalSeconds;
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name}: walks {walks.Count} minutes visited {minutes} of {bars.Count} in {seconds * 1000:F2} ms: {walks.Count / seconds:F0} walks/s, {minutes / seconds:F0} minutes/s"));
        }
    }
}
