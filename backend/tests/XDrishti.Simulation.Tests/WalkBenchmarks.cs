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

    /// <summary>
    /// P3-03d: the swing walk over 250 sessions with a hold of 1 and of 5 sessions (stops and targets far enough that most positions are held to their due minute); reports trades and minutes per second, asserts nothing.
    /// <c>dotnet test --project tests/XDrishti.Simulation.Tests -c Release -- --explicit only --filter-method "*Swing_walk_throughput*"</c>.
    /// </summary>
    [Fact(Explicit = true)]
    public void Swing_walk_throughput_for_holds_of_one_and_five_sessions_is_reported()
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
                price *= 1 + ((random.NextDouble() - 0.5) * 0.0004);
                bars.Add(new Domain.Market.Bars.Bar(DayBuilder.Epoch(date, minute), open, Math.Max(open, price) * 1.0001, Math.Min(open, price) * 0.9999, price, 1_000));
            }
        }

        foreach (var sessions in new[] { 1, 5 })
        {
            var rules = new WalkRules(2.0, WalkRules.NoSquareOff, Domain.Research.FillRules.Default, Cand.Tick05, SwingHold.Create(sessions, null).Value);
            var candidates = new List<Simulation.EntryCandidate>();
            for (var s = 0; s + 5 < Sessions; s += 6)
            {
                var index = (s * 375) + 30;
                candidates.Add(new Simulation.EntryCandidate("SWING", Cand.Test, Domain.Research.Side.Buy, index, bars[index].EpochSeconds + 60, Domain.Market.Bars.Timeframe.H1, bars[index], 60, true, bars[index].Close * 0.97, Simulation.DecisionTrace.Empty));
            }

            long minutes = 0;
            long trades = 0;
            var seconds = WarmMedianSeconds(() =>
            {
                minutes = 0;
                trades = 0;
                foreach (var candidate in candidates)
                {
                    var outcome = TradeWalker.Walk(candidate, bars, rules).Value;
                    minutes += outcome.BarsExamined;
                    trades += outcome.Trade is null ? 0 : 1;
                }
            });
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"hold {sessions}: trades {trades} minutes visited {minutes} median of {Repeats} warm runs {seconds * 1000:F3} ms: {trades / seconds:F0} trades/s, {minutes / seconds:F0} minutes/s"));
        }
    }

    private const int Repeats = 9;

    /// <summary>A warm-up of one second, then the median of <see cref="Repeats"/> timed runs, in seconds: the first run pays the JIT and says nothing of the walker.</summary>
    private static double WarmMedianSeconds(Action run)
    {
        for (var warm = Stopwatch.StartNew(); warm.Elapsed < TimeSpan.FromSeconds(1);)
        {
            run();   // long enough for the tiered JIT to promote the walker to its optimised code
        }

        var times = new List<double>();
        for (var i = 0; i < Repeats; i++)
        {
            var watch = Stopwatch.StartNew();
            run();
            watch.Stop();
            times.Add(watch.Elapsed.TotalSeconds);
        }

        times.Sort();
        return times[Repeats / 2];
    }

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
            long minutes = 0;
            var seconds = WarmMedianSeconds(() =>
            {
                minutes = 0;
                foreach (var candidate in walks)
                {
                    minutes += TradeWalker.Walk(candidate, bars, rules).Value.BarsExamined;
                }
            });
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name}: walks {walks.Count} minutes visited {minutes} of {bars.Count}, median of {Repeats} warm runs {seconds * 1000:F2} ms: {walks.Count / seconds:F0} walks/s, {minutes / seconds:F0} minutes/s"));
        }
    }
}
