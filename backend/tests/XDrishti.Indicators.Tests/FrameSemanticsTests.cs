using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>The frame's value for a feature on a timeframe is the indicator over the closed bars of that timeframe — never the one still forming (criterion 9).</summary>
public sealed class FrameSemanticsTests
{
    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 30)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void At_every_step_the_value_equals_the_reference_over_the_buckets_closed_by_then(int seed)
    {
        var bars = SeriesGenerator.Random(seed, 600, 2_500, specialSessions: true);

        foreach (var timeframe in IndicatorCatalog.SupportedTimeframes)
        {
            var specs = TestSpecs.Variants(timeframe);
            var frame = FeatureFrame.Create(specs).Value;

            // the closed bars of the timeframe in the order they close, with the step that closes each
            var closed = timeframe == Timeframe.M1
                ? bars.Select((b, i) => new ReferenceBucket(b.EpochSeconds, b.Open, b.High, b.Low, b.Close, b.Volume, 1, true, i)).ToList()
                : ReferenceBuckets.Build(bars, timeframe).Where(b => b.CloseIndex != int.MaxValue).OrderBy(b => b.CloseIndex).ThenBy(b => b.Start).ToList();
            var closedBars = closed.Select(b => b.AsBar).ToList();
            var expected = specs.Select(s => ReferenceIndicators.Compute(s, closedBars)).ToList();

            var closedSoFar = 0;
            for (var t = 0; t < bars.Count; t++)
            {
                frame.Step(bars[t]);
                var before = closedSoFar;
                while (closedSoFar < closed.Count && closed[closedSoFar].CloseIndex <= t)
                {
                    closedSoFar++;
                }

                if (frame.HasTimeframe(timeframe) && (frame.BarClosed(timeframe) != closedSoFar > before || frame.ClosedBarsAt(timeframe) != closedSoFar - before))
                {
                    Assert.Fail($"seed {seed}, {timeframe.ToCode()}, step {t}: the frame says {frame.ClosedBarsAt(timeframe)} bars closed, the reference {closedSoFar - before}");
                }

                for (var h = 0; h < specs.Count; h++)
                {
                    var want = closedSoFar == 0 ? double.NaN : expected[h][closedSoFar - 1];
                    if (!Tolerance.Same(want, frame[h]) || frame.IsReady(h) == double.IsNaN(want))
                    {
                        Assert.Fail($"seed {seed}, {TestSpecs.Describe(specs[h])}, step {t}: expected {want:R}, frame has {frame[h]:R} (ready {frame.IsReady(h)})");
                    }

                    var wantPrevious = closedSoFar < 2 ? double.NaN : expected[h][closedSoFar - 2];
                    if (!Tolerance.Same(wantPrevious, frame.At(h, 1)))
                    {
                        Assert.Fail($"seed {seed}, {TestSpecs.Describe(specs[h])}, step {t}: At(h, 1) expected {wantPrevious:R}, frame has {frame.At(h, 1):R}");
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void With_a_calendar_the_value_equals_the_reference_over_the_buckets_the_calendar_aware_reference_closes(int seed)
    {
        var bars = SeriesGenerator.Random(seed, 600, 2_500, specialSessions: true);
        var windows = CalendarFixtures.Windows(bars);
        var calendar = CalendarFixtures.For(windows);

        foreach (var timeframe in TestSpecs.HigherTimeframes)
        {
            var specs = TestSpecs.Variants(timeframe);
            var frame = FeatureFrame.Create(specs, calendar).Value;
            var closed = FrameReference.ClosedInOrder(bars, timeframe, windows);
            var closedAt = closed.Select(b => b.CloseIndex).ToList();
            var expected = specs.Select(s => ReferenceIndicators.Compute(s, [.. closed.Select(b => b.AsBar)])).ToList();

            var closedSoFar = 0;
            for (var t = 0; t < bars.Count; t++)
            {
                frame.Step(bars[t]);
                while (closedSoFar < closed.Count && closedAt[closedSoFar] <= t)
                {
                    closedSoFar++;
                }

                for (var h = 0; h < specs.Count; h++)
                {
                    var want = closedSoFar == 0 ? double.NaN : expected[h][closedSoFar - 1];
                    if (!Tolerance.Same(want, frame[h]) || frame.IsReady(h) == double.IsNaN(want))
                    {
                        Assert.Fail($"seed {seed}, with calendar, {TestSpecs.Describe(specs[h])}, step {t}: expected {want:R}, frame has {frame[h]:R}");
                    }
                }
            }
        }
    }

    [Fact]
    public void The_forming_bar_is_not_used_a_value_that_would_include_it_differs_and_is_not_what_the_frame_shows()
    {
        var day = new DateOnly(2026, 3, 2);
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M15, PriceSource.Close, 1), new FeatureSpec("highest", Timeframe.M15, PriceSource.High, 1)]).Value;
        var shown = new List<double>();

        // 09:15 to 09:29 at 100, then the next 15 minutes at 200 (a forming bar worth 200 for fourteen steps)
        for (var m = Minutes.Open; m < Minutes.Open + 30; m++)
        {
            var price = m < Minutes.Open + 15 ? 100.0 : 200.0;
            frame.Step(Minutes.Close(day, m, price));
            shown.Add(frame[0]);
        }

        shown.Take(14).ShouldAllBe(v => double.IsNaN(v), "nothing has closed before 09:29");
        shown.Skip(14).Take(15).ShouldAllBe(v => v == 100.0, "from 09:29 the frame shows the 09:15 bar, throughout the forming 09:30 bar");
        shown[29].ShouldBe(200.0, "the 09:44 bar closes the second bucket");
        frame[1].ShouldBe(200.0);
    }

    [Fact]
    public void Features_on_the_minute_timeframe_see_the_bar_at_once_and_higher_ones_only_when_it_closes()
    {
        var day = new DateOnly(2026, 3, 2);
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 1), new FeatureSpec("sma", Timeframe.M5, PriceSource.Close, 1)]).Value;

        for (var m = Minutes.Open; m < Minutes.Open + 5; m++)
        {
            frame.Step(Minutes.Close(day, m, m));
            frame[0].ShouldBe(m);
            if (m < Minutes.Open + 4)
            {
                frame.IsReady(1).ShouldBeFalse();
            }
        }

        frame[1].ShouldBe(Minutes.Open + 4);
    }
}
