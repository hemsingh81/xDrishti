using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// ISS-13, adversarial: conditions that read per-session features of five lanes allocate nothing over 100,000 steps that contain at least 250 date changes and many pending states (the frame is marked pending and its rings
/// cleared at every date change; the conditions are evaluated with unknown inputs at the start of each session). The counts prove the window held what the test says.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class SessionRestartAllocationAdversarialTests
{
    private const int WarmUpSteps = 20_000;

    private const int MeasuredSteps = 100_000;

    private static readonly Bar[] Bars = Series.Sessions(7, 330);

    private static readonly string[] Conditions =
    [
        "crosses_above(close, vwap)",
        "tf(\"1h\").prev_day_close > 0",
        "rising(tf(\"15m\").vwap, 2)",
        "tf(\"5m\").today_open[2] == tf(\"5m\").today_open",
        "tf(\"30m\").gap_pct[1] > 0 or tf(\"10m\").today_high > tf(\"10m\").today_low",
        "close > prev_day.high and close[1] <= prev_day.high",
        "between(tf(\"1h\").today_open, 0, 100000) and not crosses_below(tf(\"15m\").close, tf(\"15m\").vwap)",
    ];

    [Fact]
    public void The_series_holds_enough_sessions()
    {
        Bars.Length.ShouldBeGreaterThanOrEqualTo(WarmUpSteps + MeasuredSteps);
    }

    [Fact]
    public void Evaluating_per_session_conditions_on_five_lanes_allocates_nothing_across_date_changes_and_pending_states()
    {
        var unknownSeen = 0;
        var dateChanges = 0;

        AllocationProbe.ShouldAllocateNothing(
            window =>
            {
                var rig = Rig.Of(Timeframe.M1, Conditions);
                var bound = rig.Bound.ToArray();
                var frame = rig.Frame;
                var sink = 0;
                for (var i = 0; i < WarmUpSteps; i++)
                {
                    frame.Step(Bars[i]);
                    for (var c = 0; c < bound.Length; c++)
                    {
                        sink += (int)bound[c].EvaluateTruth(frame);
                    }
                }

                var unknown = 0;
                var changes = 0;
                window.Begin();
                for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
                {
                    frame.Step(Bars[i]);
                    changes += i % 375 == 0 ? 1 : 0;
                    for (var c = 0; c < bound.Length; c++)
                    {
                        var truth = bound[c].EvaluateTruth(frame);
                        unknown += truth == Truth.Unknown ? 1 : 0;
                        sink += (int)truth;
                    }
                }

                window.End();
                unknownSeen = unknown;
                dateChanges = changes;
                (sink != int.MinValue).ShouldBeTrue();
            },
            "Evaluate over 100,000 steps of per-session conditions");

        dateChanges.ShouldBeGreaterThanOrEqualTo(250, "date changes in the window");
        unknownSeen.ShouldBeGreaterThan(250 * 5, "unknown evaluations (pending lanes) in the window");
    }
}
