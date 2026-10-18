using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>
/// Criterion 25: <c>BoundSide.Evaluate</c> and <c>InEntryWindow</c> allocate nothing in steady state: 0 bytes over 100,000 steps of a frame of 20 features with a side of ten items, across date changes and the closes of a
/// higher timeframe. Measured with the <see cref="AllocationProbe"/> of the P3-02 tests; the tests run alone.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class SideAllocationTests
{
    private const int WarmUpSteps = 20_000;

    private const int MeasuredSteps = 100_000;

    private static readonly Bar[] Bars = Series.Sessions(99, 330);

    /// <summary>The ten conditions of the allocation test as three lists: three in the setup, four in the trigger, three in the invalidate list.</summary>
    private static CompiledSide TenItemSide()
    {
        var sources = TenConditions.Sources;
        var definition = Defs.Basic().WithBuy(b => b with
        {
            Setup = Defs.All(sources[0], sources[1], sources[2]),
            Trigger = Defs.Any(sources[3], sources[4], sources[5], sources[6]),
            Invalidate = Defs.All(sources[7], sources[8], sources[9]),
        }) with
        { Timeframes = [Timeframe.M5] };
        return Located.Ok(definition, Timeframe.M5);
    }

    private static (FeatureFrame Frame, BoundSide Bound) Build(CompiledSide side)
    {
        var context = Contexts.Covering(DateOnly.FromDateTime(Bars[0].Start.UtcDateTime), DateOnly.FromDateTime(Bars[^1].Start.UtcDateTime), 4, daysBefore: 120);
        var created = FeatureFrame.Create(side.Features, new FrameOptions { HistoryDepth = side.HistoryDepth, Context = context });
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        return (created.Value, side.Bind(created.Value));
    }

    [Fact]
    public void The_side_has_ten_items_over_at_least_twenty_features_on_five_timeframes_and_the_series_is_long_enough()
    {
        var side = TenItemSide();

        side.Items.Count.ShouldBe(10);
        side.Features.Count.ShouldBeGreaterThanOrEqualTo(20);
        side.Features.Select(f => f.Timeframe).Distinct().Count().ShouldBeGreaterThanOrEqualTo(5);
        Bars.Length.ShouldBeGreaterThanOrEqualTo(WarmUpSteps + MeasuredSteps);
    }

    [Fact]
    public void Evaluate_and_the_window_over_a_ten_item_side_allocate_nothing_across_date_changes_and_higher_timeframe_closes()
    {
        var side = TenItemSide();
        var seen = new Counts();

        AllocationProbe.ShouldAllocateNothing(
            window =>
            {
                var (frame, bound) = Build(side);
                var truths = new Truth[side.Items.Count];
                var counts = new Counts();
                Run(frame, bound, truths, 0, WarmUpSteps, counts);
                counts.Clear();
                window.Begin();
                Run(frame, bound, truths, WarmUpSteps, WarmUpSteps + MeasuredSteps, counts);
                window.End();
                seen = counts;
            },
            "Evaluate and InEntryWindow over 100,000 steps");

        seen.DateChanges.ShouldBeGreaterThanOrEqualTo(250, "date changes in the window");
        seen.DayCloses.ShouldBeGreaterThanOrEqualTo(250, "daily bar closes in the window");
        seen.HourCloses.ShouldBeGreaterThanOrEqualTo(1_500, "1-hour closes in the window");
        seen.Window.ShouldBeGreaterThan(1_000, "the window is open at some steps");
        seen.Window.ShouldBeLessThan(MeasuredSteps - 1_000, "and closed at others");
        seen.Fires.ShouldBeGreaterThanOrEqualTo(0);
        seen.Unknown.ShouldBeGreaterThan(0, "some item is unknown at some step");
    }

    [Fact]
    public void The_measurement_notices_an_allocation_in_the_evaluation_loop_when_there_is_one()
    {
        var side = TenItemSide();
        var (frame, bound) = Build(side);
        var kept = new object?[10];
        var truths = new Truth[side.Items.Count];
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
                kept[i % 10] = bound.Evaluate(frame, truths);   // boxes the struct: 32 bytes
            }

            window.End();
        });

        allocated.ShouldBeGreaterThanOrEqualTo(1_000 * 16, "a box per evaluation");
        (!disturbed || attempts == AllocationProbe.MaxAttempts).ShouldBeTrue();
        GC.KeepAlive(kept);
    }

    private sealed class Counts
    {
        public long DateChanges;

        public long HourCloses;

        public long DayCloses;

        public long Window;

        public long Fires;

        public long Unknown;

        public void Clear() => DateChanges = HourCloses = DayCloses = Window = Fires = Unknown = 0;
    }

    private static void Run(FeatureFrame frame, BoundSide bound, Truth[] truths, int from, int to, Counts counts)
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

            counts.HourCloses += frame.BarClosed(Timeframe.H1) ? 1 : 0;
            counts.DayCloses += frame.BarClosed(Timeframe.D1) ? 1 : 0;
            var evaluation = bound.Evaluate(frame, truths);
            counts.Fires += evaluation.Fires ? 1 : 0;
            counts.Window += bound.InEntryWindow(frame) ? 1 : 0;
            counts.Unknown += evaluation.Setup == Truth.Unknown || evaluation.Trigger == Truth.Unknown || evaluation.Invalidate == Truth.Unknown ? 1 : 0;
        }
    }
}
