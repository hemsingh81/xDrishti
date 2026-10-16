using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// Criterion 12: after a warm-up the steady-state <c>Evaluate</c> allocates nothing — the bytes allocated by this thread over 100,000 steps of a 20-feature frame with 10 conditions are exactly 0, across date changes
/// and the closes of the 5-minute, 15-minute, 1-hour and daily bars. Every measurement goes through <see cref="AllocationProbe"/> (a copy of the Indicators probe), which is the same as the one under the frame's own
/// allocation tests: zero bytes is final at once, bytes without a garbage collection is an allocation of the code. The bars are built before the window and the warm-up lets the JIT compile the paths first. The
/// tests run alone.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class EvaluationAllocationTests
{
    private const int WarmUpSteps = 20_000;

    private const int MeasuredSteps = 100_000;

    /// <summary>Gap-free, time-ordered bars for warm-up and measurement: 330 sessions of 375 minutes.</summary>
    private static readonly Bar[] Bars = Series.Sessions(99, 330);

    [Fact]
    public void The_series_is_long_enough_and_the_strategy_reads_twenty_features_on_five_timeframes()
    {
        Bars.Length.ShouldBeGreaterThanOrEqualTo(WarmUpSteps + MeasuredSteps);
        var plan = TenConditions.Plan();

        plan.Features.Count.ShouldBe(20);
        plan.Features.Select(f => f.Timeframe).Distinct().Order().ShouldBe([Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.D1]);
        plan.NeedsContext.ShouldBeTrue();
        plan.Conditions.Count.ShouldBe(10);
    }

    [Fact]
    public void The_ten_conditions_take_every_value_during_the_run_so_the_measurement_covers_real_work()
    {
        var (frame, bound, _) = TenConditions.Build(Bars);
        var seen = new HashSet<(int, Truth)>();

        for (var i = 0; i < WarmUpSteps + 5_000; i++)
        {
            frame.Step(Bars[i]);
            if (i >= WarmUpSteps)
            {
                for (var c = 0; c < bound.Count; c++)
                {
                    seen.Add((c, bound[c].EvaluateTruth(frame)));
                }
            }
        }

        for (var c = 0; c < bound.Count; c++)
        {
            seen.ShouldContain((c, Truth.True), "condition " + c + " is never true after the warm-up");
            seen.ShouldContain((c, Truth.False), "condition " + c + " is never false after the warm-up");
            seen.ShouldNotContain((c, Truth.Unknown), "condition " + c + " is still unknown after the warm-up");
        }
    }

    [Fact]
    public void Evaluate_of_ten_conditions_over_a_twenty_feature_frame_allocates_nothing_across_date_changes_and_higher_timeframe_closes()
    {
        var seen = new Counts();

        AllocationProbe.ShouldAllocateNothing(
            window =>
            {
                // a scenario builds what it measures, so that an attempt that has to be repeated starts from the same state
                var (frame, bound, _) = TenConditions.Build(Bars);
                var conditions = new BoundCondition[bound.Count];
                for (var c = 0; c < conditions.Length; c++)
                {
                    conditions[c] = bound[c];
                }

                var counts = new Counts();
                Run(frame, conditions, 0, WarmUpSteps, counts);
                counts.Clear();
                window.Begin();
                Run(frame, conditions, WarmUpSteps, WarmUpSteps + MeasuredSteps, counts);
                window.End();
                seen = counts;
            },
            "Evaluate over 100,000 steps");

        // the window really contained what the test says it did
        seen.DateChanges.ShouldBeGreaterThanOrEqualTo(250, "date changes in the window");
        seen.DayCloses.ShouldBeGreaterThanOrEqualTo(250, "daily bar closes in the window");
        seen.HourCloses.ShouldBeGreaterThanOrEqualTo(1_500, "1-hour closes in the window");
        seen.FifteenMinuteCloses.ShouldBeGreaterThanOrEqualTo(6_000, "15-minute closes in the window");
        seen.FiveMinuteCloses.ShouldBeGreaterThanOrEqualTo(19_000, "5-minute closes in the window");
        seen.Decisions.ShouldBeGreaterThan(1_000, "some decisions are true");
        seen.Decisions.ShouldBeLessThan(MeasuredSteps * 10L, "and some are false");
    }

    /// <summary>What a run saw, counted in fields of an object made before the window.</summary>
    private sealed class Counts
    {
        public long Decisions;

        public long DateChanges;

        public long FiveMinuteCloses;

        public long FifteenMinuteCloses;

        public long HourCloses;

        public long DayCloses;

        public void Clear() => Decisions = DateChanges = FiveMinuteCloses = FifteenMinuteCloses = HourCloses = DayCloses = 0;
    }

    /// <summary>Steps the frame over bars [from, to) and evaluates every condition after each step: the decision of each and, for every other one, the three-valued result as well.</summary>
    private static void Run(FeatureFrame frame, BoundCondition[] conditions, int from, int to, Counts counts)
    {
        var day = long.MinValue;
        for (var i = from; i < to; i++)
        {
            ref readonly var bar = ref Bars[i];
            frame.Step(in bar);
            var today = (bar.EpochSeconds + 19_800) / 86_400;
            if (today != day)
            {
                counts.DateChanges++;
                day = today;
            }

            if (frame.BarClosed(Timeframe.M5))
            {
                counts.FiveMinuteCloses++;
            }

            if (frame.BarClosed(Timeframe.M15))
            {
                counts.FifteenMinuteCloses++;
            }

            if (frame.BarClosed(Timeframe.H1))
            {
                counts.HourCloses++;
            }

            if (frame.BarClosed(Timeframe.D1))
            {
                counts.DayCloses++;
            }

            for (var c = 0; c < conditions.Length; c++)
            {
                if (conditions[c].Evaluate(frame))
                {
                    counts.Decisions++;
                }

                if ((c & 1) == 0 && conditions[c].EvaluateTruth(frame) == Truth.Unknown)
                {
                    counts.Decisions--;
                }
            }
        }
    }

    [Fact]
    public void The_measurement_notices_an_allocation_in_the_evaluation_loop_when_there_is_one()
    {
        var (frame, bound, _) = TenConditions.Build(Bars);
        var conditions = bound.ToArray();
        var kept = new object?[conditions.Length];
        for (var i = 0; i < 6_000; i++)
        {
            frame.Step(Bars[i]);
        }

        var (allocated, disturbed, attempts) = AllocationProbe.Measure(window =>
        {
            window.Begin();
            for (var i = 6_000; i < 7_000; i++)
            {
                frame.Step(Bars[i]);
                for (var c = 0; c < conditions.Length; c++)
                {
                    kept[c] = conditions[c].EvaluateTruth(frame);   // boxes the enum: 24 bytes
                }
            }

            window.End();
        });

        allocated.ShouldBeGreaterThanOrEqualTo(1_000 * 10 * 24 - 24 * 10 * 4, "a box per evaluation");
        (!disturbed || attempts == AllocationProbe.MaxAttempts).ShouldBeTrue();
        GC.KeepAlive(kept);
    }

    [Fact]
    public void Evaluating_a_frame_that_does_not_move_a_million_times_allocates_nothing_for_every_node_kind()
    {
        string[] sources =
        [
            "close > open", "close >= open", "close < open", "close <= open", "close == open", "close != open",
            "close + open > 1", "close - open < 1", "close * 2 > open / 2", "-close < -open", "abs(close - open) >= 0", "min(close, open) <= max(close, open)",
            "(close > open) == (open > low)", "(close > open) != (open > low)", "not (close > open) or close > low and open < high",
            "crosses_above(close, open)", "crosses_below(close, ema(3) + 1)", "rising(close, 5)", "falling(close - open, 3)", "rising(close[2] * 2, 40)", "between(close, low, high)",
            "close[3] > open[5]", "time >= \"09:45\"", "day_of_week == TUE or day_of_week != WED", "close / 0 > 1", "true and close > 0", "1 < 2",
        ];
        var rig = Rig.Of(Timeframe.M1, sources, minimumDepth: 1);
        foreach (var bar in Bars.Take(2_000))
        {
            rig.Frame.Step(bar);
        }

        var frame = rig.Frame;
        var bound = rig.Bound.ToArray();
        long sum = 0;
        for (var round = 0; round < 1_000; round++)
        {
            foreach (var condition in bound)
            {
                sum += condition.Evaluate(frame) ? 1 : 0;
                sum += (int)condition.EvaluateTruth(frame);
            }
        }

        AllocationProbe.ShouldAllocateNothing(
            window =>
            {
                window.Begin();
                for (var round = 0; round < 40_000; round++)
                {
                    for (var c = 0; c < bound.Length; c++)
                    {
                        sum += bound[c].Evaluate(frame) ? 1 : 0;
                        sum += (int)bound[c].EvaluateTruth(frame);
                    }
                }

                window.End();
            },
            "40,000 rounds of 27 conditions on a frame that does not move");

        (sum != 0).ShouldBeTrue("the work was done");
    }
}
