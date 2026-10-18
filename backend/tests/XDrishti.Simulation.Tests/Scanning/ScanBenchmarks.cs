using System.Diagnostics;
using System.Globalization;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>
/// Criterion 17c of P3-03a (the scanner's part; the walk is added with the walker): the explicit benchmark, not part of <c>scripts/check.sh</c>. Replays 250 seeded sessions (93,750 minutes) and prints minutes per second for the
/// frame alone and for the scan of one side and of both, the share of the scan spent evaluating conditions (the cost lint for long <c>rising</c>/<c>falling</c> runs, ADR 0034 Amendment 18, is opened only if that share exceeds
/// about 20 %) and the cost of two frames per instrument against the estimate of P3-04. Reported and not asserted; run with <c>--explicit only</c>.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class ScanBenchmarks
{
    private static double MinutesPerSecond(int minutes, Action run)
    {
        run();
        var clock = Stopwatch.StartNew();
        run();
        return minutes / clock.Elapsed.TotalSeconds;
    }

    [Fact(Explicit = true)]
    public void Report_minutes_per_second_for_the_frame_the_scan_of_one_side_and_both_sides()
    {
        var bars = BenchmarkSessions.Minutes();
        var buy = Scan.Compile(OrbV1.Create(), Side.Buy);
        var sell = Scan.Compile(OrbV1.Create(), Side.Sell);

        var frameOnly = MinutesPerSecond(bars.Length, () =>
        {
            var frame = FeatureFrame.Create(buy.Features, new FrameOptions(null, buy.HistoryDepth)).Value;
            foreach (var bar in bars)
            {
                frame.Step(bar);
            }
        });
        var oneSide = MinutesPerSecond(bars.Length, () => Scan.Run(buy, bars));
        var bothSides = MinutesPerSecond(bars.Length, () =>
        {
            Scan.Run(buy, bars);
            Scan.Run(sell, bars);
        });

        // the time of the scan that is not the frame's: evaluation, window, counting (a minute of the scan takes 1 / oneSide, of the frame alone 1 / frameOnly)
        var share = 1 - oneSide / frameOnly;
        TestContext.Current.TestOutputHelper?.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0:N0} minutes; frame alone {1:N0} min/s; scan of one side {2:N0} min/s; both sides (two frames) {3:N0} min/s; evaluation and counting share of the one-side scan {4:P1}; 214 instruments x 190,000 minutes at the two-frame rate: {5:N0} s",
            bars.Length,
            frameOnly,
            oneSide,
            bothSides,
            share,
            214 * 190_000 / bothSides));
    }
}
