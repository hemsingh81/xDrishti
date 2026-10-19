using System.Diagnostics;
using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>
/// Criterion 16 of P3-05. The scanner allocates 0 bytes in steady state on sessions of this strategy that fire nothing (the shape of criterion 17a of P3-03a on the day of V3: the day of V3 is the warm-up, the sessions measured after it are flat, so no trigger is true),
/// one frame step is taken per minute, and the explicit benchmark prints the share of the scan spent evaluating conditions for <c>VWAP_PULLBACK</c> next to <c>ORB</c>'s, the figure of ISS-26 (not part of <c>scripts/check.sh</c>; run with <c>--explicit only</c>).
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class VwapScanWorkTests
{
    private static readonly DateOnly[] Dates = [new(2026, 3, 3), new(2026, 3, 4), new(2026, 3, 5), new(2026, 3, 6), new(2026, 3, 9), new(2026, 3, 10), new(2026, 3, 11)];

    [Theory]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void Six_sessions_that_fire_nothing_allocate_nothing_after_the_warm_up_of_the_prefix_and_one_session(Side side)
    {
        var warmUp = VwapDays.V3().Concat(BandedDays.Session(Dates[0], BandedDays.Pivot).Bars()).ToArray();
        var measured = Dates.Skip(1).Select(d => BandedDays.Session(d, BandedDays.Pivot).Bars()).ToArray();
        var minutes = measured.Sum(s => s.Length);
        EntryScanner? seen = null;

        AllocationProbe.ShouldAllocateNothing(
            window =>
            {
                var scanner = Scan.Scanner(Scan.Compile(VwapPullback.Create(), side));
                foreach (var bar in warmUp)
                {
                    scanner.Step(bar);
                }

                window.Begin();
                foreach (var session in measured)
                {
                    for (var i = 0; i < session.Length; i++)
                    {
                        scanner.Step(session[i]);
                    }
                }

                window.End();
                seen = scanner;
            },
            "six flat sessions behind the day of V3 through the scanner");

        seen!.Candidates.ShouldBeEmpty();
        seen.Counters.Bars.ShouldBe(warmUp.Length + minutes);
        seen.Counters.DecisionSteps.ShouldBe((22 + 7) * 25);
    }

    [Fact]
    public void The_frame_takes_one_step_per_minute_on_the_hand_days()
    {
        foreach (var bars in new[] { VwapDays.V1(), VwapDays.V3(), VwapDays.V6() })
        {
            foreach (var side in new[] { Side.Buy, Side.Sell })
            {
                var scanner = Scan.Run(Scan.Compile(VwapPullback.Create(), side), bars);

                scanner.Counters.Bars.ShouldBe(bars.Length);
                Identities.ShouldHold(scanner);
            }
        }
    }

    private static double MinutesPerSecond(int minutes, Action run)
    {
        run();
        var clock = Stopwatch.StartNew();
        run();
        return minutes / clock.Elapsed.TotalSeconds;
    }

    private static double ConditionShare(CompiledSide side, Bar[] bars)
    {
        var frameOnly = MinutesPerSecond(bars.Length, () =>
        {
            var frame = FeatureFrame.Create(side.Features, new FrameOptions(null, side.HistoryDepth)).Value;
            foreach (var bar in bars)
            {
                frame.Step(bar);
            }
        });
        var oneSide = MinutesPerSecond(bars.Length, () => Scan.Run(side, bars));
        return 1 - oneSide / frameOnly;
    }

    [Fact(Explicit = true)]
    public void Report_the_share_of_the_scan_spent_on_conditions_for_vwap_pullback_next_to_orb()
    {
        var orbBars = BenchmarkSessions.Minutes();
        var vwapBars = VwapSeeded.Of(7, days: BenchmarkSessions.Sessions - 21);

        var orb = ConditionShare(Scan.Compile(OrbV1.Create(), Side.Buy), orbBars);
        var vwap = ConditionShare(Scan.Compile(VwapPullback.Create(), Side.Buy), vwapBars);

        TestContext.Current.TestOutputHelper?.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "non-frame share of a one-side scan: ORB {0:P1} ({1:N0} minutes), VWAP_PULLBACK {2:P1} ({3:N0} minutes)",
            orb,
            orbBars.Length,
            vwap,
            vwapBars.Length));
    }
}
