using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// The value history of the frame (criterion 2 of P3-01b): <c>At(handle, lag)</c> is the value the feature had <c>lag</c> closed bars of its own timeframe ago, written only when that timeframe closes a bar.
/// Compared with an independent record: the reference series over the closed bars of each timeframe.
/// </summary>
public sealed class FrameHistoryTests
{
    public static TheoryData<int, int> DepthsAndSeeds
    {
        get
        {
            var data = new TheoryData<int, int>();
            foreach (var depth in new[] { 1, 2, 8, 256 })
            {
                foreach (var seed in Enumerable.Range(1, 6))
                {
                    data.Add(depth, seed);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DepthsAndSeeds))]
    public void At_equals_the_reference_value_k_closed_bars_ago_for_every_feature_including_not_ready_lags(int depth, int seed)
    {
        var bars = SeriesGenerator.Random(seed, 600, 2_000, specialSessions: true);
        var specs = new[] { Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.D1 }.SelectMany(TestSpecs.Variants).ToList();
        var frame = FeatureFrame.Create(specs, new FrameOptions(HistoryDepth: depth)).Value;

        // the independent record: per timeframe the closed bars in closing order and the reference series of each spec over them
        var closedByStep = IndicatorCatalog.SupportedTimeframes.ToDictionary(t => t, t => FrameReference.ClosedByStep(bars, t));
        var expected = specs.Select(s => ReferenceIndicators.Compute(s, [.. closedByStep[s.Timeframe].SelectMany(x => x).Select(b => b.AsBar)])).ToList();
        var closedSoFar = specs.Select(_ => 0).ToArray();
        frame.HistoryDepth.ShouldBe(depth);

        for (var t = 0; t < bars.Count; t++)
        {
            frame.Step(bars[t]);
            var everyLag = t % 61 == 0 || t == bars.Count - 1;
            for (var h = 0; h < specs.Count; h++)
            {
                closedSoFar[h] += closedByStep[specs[h].Timeframe][t].Count;
                var lags = everyLag ? Enumerable.Range(0, depth) : new[] { 0, 1, depth - 1 }.Where(l => l < depth).Distinct();
                foreach (var lag in lags)
                {
                    var index = closedSoFar[h] - 1 - lag;
                    var want = index < 0 ? double.NaN : expected[h][index];
                    var got = frame.At(h, lag);
                    if (!Tolerance.Same(want, got) || frame.IsReadyAt(h, lag) == double.IsNaN(want))
                    {
                        Assert.Fail($"depth {depth}, seed {seed}, {TestSpecs.Describe(specs[h])}, step {t}, lag {lag}: expected {want:R}, At is {got:R} (ready {frame.IsReadyAt(h, lag)})");
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(DepthsAndSeeds))]
    public void The_history_shifts_only_when_the_features_own_timeframe_closes_and_by_the_number_of_bars_it_closed(int depth, int seed)
    {
        var bars = SeriesGenerator.Random(seed + 100, 500, 1_500, specialSessions: true);
        var timeframes = new[] { Timeframe.M1, Timeframe.M5, Timeframe.H1, Timeframe.D1 };
        var specs = timeframes.SelectMany(TestSpecs.Variants).ToList();
        var frame = FeatureFrame.Create(specs, new FrameOptions(HistoryDepth: depth)).Value;
        var before = new double[specs.Count][];
        for (var h = 0; h < specs.Count; h++)
        {
            before[h] = new double[depth];
            Array.Fill(before[h], double.NaN);
        }

        for (var t = 0; t < bars.Count; t++)
        {
            frame.Step(bars[t]);
            foreach (var timeframe in timeframes)
            {
                var closed = frame.ClosedBarsAt(timeframe);
                for (var h = 0; h < specs.Count; h++)
                {
                    if (specs[h].Timeframe != timeframe)
                    {
                        continue;
                    }

                    for (var lag = 0; lag < depth; lag++)
                    {
                        var got = frame.At(h, lag);
                        // after c closes, the value at lag k + c is the one that was at lag k before the step (a recorded value is never rewritten)
                        if (closed == 0 || lag >= closed)
                        {
                            var want = lag - closed >= 0 ? before[h][lag - closed] : double.NaN;
                            if (!Tolerance.Identical(want, got))
                            {
                                Assert.Fail($"depth {depth}, seed {seed}, {TestSpecs.Describe(specs[h])}, step {t}, lag {lag}: was {want:R} before the step ({closed} bars closed), now {got:R}");
                            }
                        }
                    }
                }
            }

            for (var h = 0; h < specs.Count; h++)
            {
                for (var lag = 0; lag < depth; lag++)
                {
                    before[h][lag] = frame.At(h, lag);
                }
            }
        }
    }

    [Fact]
    public void Lag_zero_is_the_current_value_and_the_readiness_follows_it()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 3)]).Value;

        foreach (var bar in Minutes.Closes(BarFixtures.Mon, 3, 6, 9, 12, 15))
        {
            frame.Step(bar);
            Tolerance.Identical(frame.At(0, 0), frame[0]).ShouldBeTrue();
            frame.IsReadyAt(0, 0).ShouldBe(frame.IsReady(0));
        }
    }

    [Fact]
    public void Lags_before_the_feature_was_ready_are_not_ready_and_nan()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 3)]).Value;
        var bars = Minutes.Closes(BarFixtures.Mon, 3, 6, 9, 12, 15);

        frame.Step(bars[0]);
        frame.Step(bars[1]);
        frame.IsReadyAt(0, 0).ShouldBeFalse();
        frame.At(0, 1).ShouldBeNaN();

        frame.Step(bars[2]);   // first value 6
        frame.At(0, 0).ShouldBe(6.0);
        frame.IsReadyAt(0, 1).ShouldBeFalse("the value after bar 2 was not ready");
        frame.At(0, 1).ShouldBeNaN();
        frame.At(0, 7).ShouldBeNaN("more lags than bars closed (within the depth)");

        frame.Step(bars[3]);   // 9
        (frame.At(0, 0), frame.At(0, 1)).ShouldBe((9.0, 6.0));
        frame.IsReadyAt(0, 1).ShouldBeTrue();
        frame.IsReadyAt(0, 2).ShouldBeFalse();

        frame.Step(bars[4]);   // 12
        (frame.At(0, 0), frame.At(0, 1), frame.At(0, 2)).ShouldBe((12.0, 9.0, 6.0));
        frame.IsReadyAt(0, 3).ShouldBeFalse();
    }

    [Fact]
    public void A_higher_timeframe_feature_does_not_move_its_history_between_closes_and_shifts_on_the_close()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.M5)]).Value;
        var day = BarFixtures.Mon;

        // three 5-minute bars with closes 5, 10, 15 (the close of each bucket is that of its last minute)
        var seen = new List<(double Now, double Previous, double Before)>();
        for (var m = Minutes.Open; m < Minutes.Open + 15; m++)
        {
            frame.Step(Minutes.Close(day, m, m - Minutes.Open + 1));
            seen.Add((frame.At(0, 0), frame.At(0, 1), frame.At(0, 2)));
        }

        seen.Take(4).ShouldAllBe(x => double.IsNaN(x.Now) && double.IsNaN(x.Previous));
        seen.Skip(4).Take(5).ShouldAllBe(x => x.Now == 5.0 && double.IsNaN(x.Previous));
        seen.Skip(9).Take(5).ShouldAllBe(x => x.Now == 10.0 && x.Previous == 5.0 && double.IsNaN(x.Before));
        seen[14].ShouldBe((15.0, 10.0, 5.0));
    }

    [Fact]
    public void A_minute_that_closes_two_buckets_writes_two_history_entries()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.M5)]).Value;
        frame.Step(Minutes.Bar(BarFixtures.Mon, Minutes.Open, 10, 12, 9, 11, 5));

        frame.Step(Minutes.Bar(BarFixtures.Mon, Minutes.Open + 9, 20, 22, 19, 21, 7));

        frame.ClosedBarsAt(Timeframe.M5).ShouldBe(2);
        (frame.At(0, 0), frame.At(0, 1)).ShouldBe((21.0, 11.0));
        frame.IsReadyAt(0, 2).ShouldBeFalse();
    }

    [Fact]
    public void The_history_runs_across_session_boundaries_without_a_break()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.D1)]).Value;
        var days = new[] { BarFixtures.Mon, BarFixtures.Tue, BarFixtures.Wed };
        foreach (var (day, index) in days.Select((d, i) => (d, i)))
        {
            foreach (var bar in BarFixtures.Walk(day, Minutes.Open, 375))
            {
                frame.Step(bar);
            }

            frame.At(0, 0).ShouldBe(BarFixtures.Walk(day, Minutes.Open, 375)[^1].Close);
            if (index > 0)
            {
                frame.At(0, 1).ShouldBe(BarFixtures.Walk(days[index - 1], Minutes.Open, 375)[^1].Close);
            }
        }

        frame.At(0, 2).ShouldBe(BarFixtures.Walk(BarFixtures.Mon, Minutes.Open, 375)[^1].Close);
    }

    [Fact]
    public void Specs_for_the_same_feature_share_one_history()
    {
        var spec = new FeatureSpec("ema", Timeframe.M1, PriceSource.Close, 3);
        var frame = FeatureFrame.Create([spec, new FeatureSpec("ema", Timeframe.M1, PriceSource.Close, 5), spec]).Value;
        foreach (var bar in Minutes.Closes(BarFixtures.Mon, 1, 3, 2, 5, 4, 6))
        {
            frame.Step(bar);
        }

        for (var lag = 0; lag < 4; lag++)
        {
            Tolerance.Identical(frame.At(0, lag), frame.At(2, lag)).ShouldBeTrue($"lag {lag}");
        }

        frame.At(0, 1).ShouldNotBe(frame.At(1, 1));
    }

    [Fact]
    public void Reset_clears_the_history()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.M1)]).Value;
        foreach (var bar in Minutes.Closes(BarFixtures.Mon, 1, 2, 3))
        {
            frame.Step(bar);
        }

        frame.Reset();

        for (var lag = 0; lag < frame.HistoryDepth; lag++)
        {
            frame.At(0, lag).ShouldBeNaN();
            frame.IsReadyAt(0, lag).ShouldBeFalse();
        }

        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open, 9));
        (frame.At(0, 0), frame.IsReadyAt(0, 1)).ShouldBe((9.0, false));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(256)]
    public void The_history_depth_is_the_number_of_readable_lags(int depth)
    {
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.M1)], new FrameOptions(HistoryDepth: depth)).Value;

        frame.At(0, depth - 1).ShouldBeNaN();
        Should.Throw<ArgumentOutOfRangeException>(() => frame.At(0, depth));
        Should.Throw<ArgumentOutOfRangeException>(() => frame.IsReadyAt(0, depth));
        Should.Throw<ArgumentOutOfRangeException>(() => frame.At(0, -1));
    }

    [Fact]
    public void The_default_depth_is_eight()
    {
        FeatureFrame.Create([new FeatureSpec("close")]).Value.HistoryDepth.ShouldBe(8);
        new FrameOptions().HistoryDepth.ShouldBe(8);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(257)]
    public void A_history_depth_outside_one_to_two_hundred_fifty_six_is_refused_with_its_field(int depth)
    {
        var result = FeatureFrame.Create([new FeatureSpec("close")], new FrameOptions(HistoryDepth: depth));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(FeatureFrame.InvalidCode);
        result.Error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("options.historyDepth");
    }

    [Fact]
    public void A_bad_depth_and_a_bad_spec_are_reported_together()
    {
        var result = FeatureFrame.Create([new FeatureSpec("nope")], new FrameOptions(HistoryDepth: 0));

        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["options.historyDepth", "specs[0].name"]);
    }
}
