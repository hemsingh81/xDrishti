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
        var sessionBars = specs.Select(_ => new SessionBars()).ToArray();
        frame.HistoryDepth.ShouldBe(depth);

        for (var t = 0; t < bars.Count; t++)
        {
            frame.Step(bars[t]);
            var everyLag = t % 61 == 0 || t == bars.Count - 1;
            var minuteDate = SessionClock.DateOf(bars[t].EpochSeconds);
            for (var h = 0; h < specs.Count; h++)
            {
                closedSoFar[h] += closedByStep[specs[h].Timeframe][t].Count;
                foreach (var bucket in closedByStep[specs[h].Timeframe][t])
                {
                    sessionBars[h].Closed(bucket);
                }

                // ADR 0035: the twelve per-session features on an intraday timeframe keep the history of the date of the minute just fed only (S bars); before it the history ran on by position, which is what
                // this test pinned (P3-01b criterion 2, "including across session boundaries") for every feature, and still does for the others
                var known = SessionBars.Restarts(specs[h]) ? sessionBars[h].Known(minuteDate) : int.MaxValue;
                var lags = everyLag ? Enumerable.Range(0, depth) : new[] { 0, 1, depth - 1 }.Where(l => l < depth).Distinct();
                foreach (var lag in lags)
                {
                    var index = closedSoFar[h] - 1 - lag;
                    var want = index < 0 || lag >= known ? double.NaN : expected[h][index];
                    var got = frame.At(h, lag);
                    if (!Tolerance.Same(want, got) || frame.IsReadyAt(h, lag) == double.IsNaN(want))
                    {
                        Assert.Fail($"depth {depth}, seed {seed}, {TestSpecs.Describe(specs[h])}, step {t}, lag {lag}: expected {want:R}, At is {got:R} (ready {frame.IsReadyAt(h, lag)})");
                    }
                }
            }
        }
    }

    public static TheoryData<int, int> PeriodDepthsAndSeeds
    {
        get
        {
            var data = new TheoryData<int, int>();
            foreach (var depth in new[] { 1, 2, 8, 256 })
            {
                foreach (var seed in Enumerable.Range(1, 3))
                {
                    data.Add(depth, seed);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(PeriodDepthsAndSeeds))]
    public void At_equals_the_reference_value_k_closed_weeks_or_months_ago_for_every_weekly_and_monthly_feature_including_not_ready_lags(int depth, int seed)
    {
        // 1,400 calendar days: about 200 weeks and 46 months, so the long warm-ups are crossed; the calendar the frame is given varies with the seed
        var series = SeriesGenerator.Thin(seed, 1_400);
        var known = PeriodCalendars.For(series, (CalendarKnowledge)(seed % 4));
        var specs = TestSpecs.PeriodTimeframes.SelectMany(TestSpecs.Variants).ToList();
        var frame = FeatureFrame.Create(specs, known.Options(depth)).Value;
        var closedByStep = TestSpecs.PeriodTimeframes.ToDictionary(t => t, t => FrameReference.ClosedByStep(series.Bars, t, known.Sessions, known.Holidays));
        var closedOfHandle = specs.Select(s => closedByStep[s.Timeframe]).ToArray();
        var expected = specs.Select((s, h) => ReferenceIndicators.Compute(s, [.. closedOfHandle[h].SelectMany(x => x).Select(b => b.AsBar)])).ToList();
        var closedSoFar = new int[specs.Count];

        for (var t = 0; t < series.Bars.Count; t++)
        {
            frame.Step(series.Bars[t]);
            var anyClosed = frame.BarClosed(Timeframe.W1) || frame.BarClosed(Timeframe.Mo1);
            var everyLag = anyClosed || t % 61 == 0 || t == series.Bars.Count - 1;
            for (var h = 0; h < specs.Count; h++)
            {
                closedSoFar[h] += closedOfHandle[h][t].Count;
                if (!everyLag && h != 0 && specs[h].Timeframe == specs[h - 1].Timeframe)
                {
                    continue;
                }

                for (var lag = 0; lag < depth; lag++)
                {
                    if (!everyLag && lag > 1 && lag < depth - 1)
                    {
                        continue;
                    }

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
    [MemberData(nameof(PeriodDepthsAndSeeds))]
    public void The_weekly_and_monthly_history_shifts_only_when_the_features_own_timeframe_closes_and_by_the_number_of_bars_it_closed(int depth, int seed)
    {
        var series = SeriesGenerator.Thin(seed + 100, 700);
        var known = PeriodCalendars.For(series, (CalendarKnowledge)(seed % 2 + 1));
        var timeframes = new[] { Timeframe.D1, Timeframe.W1, Timeframe.Mo1 };
        var specs = timeframes.SelectMany(TestSpecs.Representatives).ToList();
        var frame = FeatureFrame.Create(specs, known.Options(depth)).Value;
        var before = new double[specs.Count][];
        for (var h = 0; h < specs.Count; h++)
        {
            before[h] = new double[depth];
            Array.Fill(before[h], double.NaN);
        }

        var closes = new int[timeframes.Length];
        for (var t = 0; t < series.Bars.Count; t++)
        {
            frame.Step(series.Bars[t]);
            for (var i = 0; i < timeframes.Length; i++)
            {
                closes[i] += frame.ClosedBarsAt(timeframes[i]);
            }

            // Every lag is checked at the steps where the feature's timeframe closed a bar and at every 37th step; at the others lags 0, 1 and the last (a bug that moved the history between closes shows at the next close)
            var fullStep = t % 37 == 0 || t == series.Bars.Count - 1;
            for (var h = 0; h < specs.Count; h++)
            {
                var closed = frame.ClosedBarsAt(specs[h].Timeframe);
                var all = closed > 0 || fullStep;
                for (var lag = 0; lag < depth; lag++)
                {
                    if (!all && lag > 1 && lag < depth - 1)
                    {
                        continue;
                    }

                    // after c closes, the value at lag k + c is the one that was at lag k before the step; with no close nothing moves
                    if (closed == 0 || lag >= closed)
                    {
                        var want = lag - closed >= 0 ? before[h][lag - closed] : double.NaN;
                        var got = frame.At(h, lag);
                        if (!Tolerance.Identical(want, got))
                        {
                            Assert.Fail($"depth {depth}, seed {seed}, {TestSpecs.Describe(specs[h])}, step {t}, lag {lag}: was {want:R} before the step ({closed} bars closed), now {got:R}");
                        }
                    }
                }
            }

            for (var h = 0; h < specs.Count; h++)
            {
                var all = frame.ClosedBarsAt(specs[h].Timeframe) > 0 || fullStep;
                for (var lag = 0; lag < depth; lag++)
                {
                    if (all || lag <= 1 || lag == depth - 1)
                    {
                        before[h][lag] = frame.At(h, lag);
                    }
                }
            }
        }

        closes[1].ShouldBeGreaterThan(70, "weeks closed");
        closes[2].ShouldBeGreaterThan(15, "months closed");
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

            // ADR 0035: at the first minute of a new India date the history of a per-session feature on an intraday timeframe is forgotten, so the shift invariant is for the other steps (and for every other feature)
            var newDate = t > 0 && SessionClock.DateOf(bars[t].EpochSeconds) != SessionClock.DateOf(bars[t - 1].EpochSeconds);
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
                        if ((closed == 0 || lag >= closed) && !(newDate && SessionBars.Restarts(specs[h])))
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
    public void The_session_values_on_a_daily_lane_run_across_the_days_without_a_break_nothing_restarts_on_1D()
    {
        // ADR 0035 restarts the twelve per-session names on 1m to 1h only: a daily bar is a whole session, so a lag on 1D is by position. today_open of a day is its first bar's open, gap_pct is
        // (that open - the last close of the day before) / that close · 100, both read from the fixture's own bars
        var frame = FeatureFrame.Create([new FeatureSpec("today_open", Timeframe.D1), new FeatureSpec("gap_pct", Timeframe.D1), new FeatureSpec("prev_day_close", Timeframe.D1)]).Value;
        var days = new[] { BarFixtures.Mon, BarFixtures.Tue, BarFixtures.Wed };
        var series = days.Select(d => BarFixtures.Walk(d, Minutes.Open, 375)).ToList();
        double Gap(int day) => (series[day][0].Open - series[day - 1][^1].Close) / series[day - 1][^1].Close * 100;

        foreach (var (bars, index) in series.Select((b, i) => (b, i)))
        {
            foreach (var bar in bars)
            {
                frame.Step(bar);
            }

            frame.At(0, 0).ShouldBe(bars[0].Open);
            if (index > 0)
            {
                frame.At(0, 1).ShouldBe(series[index - 1][0].Open, "yesterday's open one daily bar back");
                frame.At(2, 0).ShouldBe(series[index - 1][^1].Close, "the previous day's close");
                frame.At(1, 0).ShouldBe(Gap(index), 1e-9);
            }

            if (index > 1)
            {
                frame.At(1, 1).ShouldBe(Gap(index - 1), 1e-9, "gap_pct one daily bar back is yesterday's gap");
                frame.At(2, 1).ShouldBe(series[index - 2][^1].Close, "prev_day_close one back is the close of the day before yesterday");
            }
        }

        frame.At(0, 2).ShouldBe(series[0][0].Open);
    }

    [Fact]
    public void A_monthly_feature_moves_its_history_on_the_close_of_the_last_session_of_the_month_and_at_no_other_step()
    {
        // Jan 31 and Feb 28 are Saturdays, Mar 31 a Tuesday and Apr 30 a Thursday: the last sessions are Jan 30, Feb 27, Mar 31 and Apr 30
        (DateOnly Date, double Close)[] days =
        [
            (new DateOnly(2026, 1, 29), 100), (new DateOnly(2026, 1, 30), 110), (new DateOnly(2026, 2, 2), 120), (new DateOnly(2026, 2, 27), 130),
            (new DateOnly(2026, 3, 2), 140), (new DateOnly(2026, 3, 31), 150), (new DateOnly(2026, 4, 1), 160), (new DateOnly(2026, 4, 30), 170),
        ];
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.Mo1), new FeatureSpec("sma", Timeframe.Mo1, PriceSource.Close, 2)], new FrameOptions(new XDrishti.Domain.Market.Calendar.TradingCalendar())).Value;
        var seen = new List<(DateOnly Date, int Minute, double Now, double Previous, double BeforeThat, double Mean)>();

        foreach (var (date, close) in days)
        {
            foreach (var bar in BarFixtures.Brief(date, close, close + 5, close - 5, close, 1_000))
            {
                frame.Step(bar);
                seen.Add((date, SessionClock.MinuteOfDay(bar.EpochSeconds), frame.At(0, 0), frame.At(0, 1), frame.At(0, 2), frame[1]));
            }
        }

        double Nan() => double.NaN;
        seen.Where(x => x.Date < new DateOnly(2026, 1, 30) || (x.Date == new DateOnly(2026, 1, 30) && x.Minute == Minutes.Open)).ShouldAllBe(x => double.IsNaN(x.Now), "January has not closed");
        var january = seen.Single(x => x.Date == new DateOnly(2026, 1, 30) && x.Minute == Minutes.At(15, 29));
        (january.Now, january.Previous, january.Mean).ShouldBe((110.0, Nan(), Nan()));
        seen.Where(x => x.Date > new DateOnly(2026, 1, 30) && x.Date < new DateOnly(2026, 2, 27)).ShouldAllBe(x => x.Now == 110.0 && double.IsNaN(x.Previous), "February is the month in progress");
        var february = seen.Single(x => x.Date == new DateOnly(2026, 2, 27) && x.Minute == Minutes.At(15, 29));
        (february.Now, february.Previous, february.BeforeThat, february.Mean).ShouldBe((130.0, 110.0, Nan(), 120.0));
        var march = seen.Single(x => x.Date == new DateOnly(2026, 3, 31) && x.Minute == Minutes.At(15, 29));
        (march.Now, march.Previous, march.BeforeThat, march.Mean).ShouldBe((150.0, 130.0, 110.0, 140.0));
        var april = seen.Last();
        (april.Now, april.Previous, april.BeforeThat, april.Mean).ShouldBe((170.0, 150.0, 130.0, 160.0));
        seen.Where(x => x.Date == new DateOnly(2026, 4, 1)).ShouldAllBe(x => x.Now == 150.0 && x.Previous == 130.0, "April has begun and moves nothing");
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
