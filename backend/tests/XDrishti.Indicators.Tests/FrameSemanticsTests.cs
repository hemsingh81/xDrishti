using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>The frame's value for a feature on a timeframe is the indicator over the closed bars of that timeframe — never the one still forming (criterion 9).</summary>
public sealed class FrameSemanticsTests
{
    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 30)];

    public static TheoryData<int, CalendarKnowledge> PeriodSeeds
    {
        get
        {
            var data = new TheoryData<int, CalendarKnowledge>();
            foreach (var seed in Enumerable.Range(1, 24))
            {
                data.Add(seed, (CalendarKnowledge)(seed % 4));
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void At_every_step_the_value_equals_the_reference_over_the_buckets_closed_by_then(int seed)
    {
        var bars = SeriesGenerator.Random(seed, 600, 2_500, specialSessions: true);

        foreach (var timeframe in TestSpecs.UpToDay)
        {
            var specs = TestSpecs.Variants(timeframe);
            var frame = FeatureFrame.Create(specs).Value;

            // the closed bars of the timeframe in the order they close, with the step that closes each (a week or a month hardly closes within a few sessions: the long series of the theories below test those)
            var closed = FrameReference.ClosedInOrder(bars, timeframe);
            var closedBars = closed.Select(b => b.AsBar).ToList();
            var expected = specs.Select(s => ReferenceIndicators.Compute(s, closedBars)).ToList();

            var closedSoFar = 0;
            var sessionBars = new SessionBars();
            for (var t = 0; t < bars.Count; t++)
            {
                frame.Step(bars[t]);
                var before = closedSoFar;
                while (closedSoFar < closed.Count && closed[closedSoFar].CloseIndex <= t)
                {
                    sessionBars.Closed(closed[closedSoFar]);
                    closedSoFar++;
                }

                var minuteDate = SessionClock.DateOf(bars[t].EpochSeconds);

                if (frame.HasTimeframe(timeframe) && (frame.BarClosed(timeframe) != closedSoFar > before || frame.ClosedBarsAt(timeframe) != closedSoFar - before))
                {
                    Assert.Fail($"seed {seed}, {timeframe.ToCode()}, step {t}: the frame says {frame.ClosedBarsAt(timeframe)} bars closed, the reference {closedSoFar - before}");
                }

                for (var h = 0; h < specs.Count; h++)
                {
                    // ADR 0035: of the twelve per-session features on an intraday timeframe only the bars closed in the date of the minute just fed are known (S of them); the reference runs over all the closed bars
                    var known = SessionBars.Restarts(specs[h]) ? sessionBars.Known(minuteDate) : int.MaxValue;
                    var want = closedSoFar == 0 || known < 1 ? double.NaN : expected[h][closedSoFar - 1];
                    if (!Tolerance.Same(want, frame[h]) || frame.IsReady(h) == double.IsNaN(want))
                    {
                        Assert.Fail($"seed {seed}, {TestSpecs.Describe(specs[h])}, step {t}: expected {want:R}, frame has {frame[h]:R} (ready {frame.IsReady(h)})");
                    }

                    var wantPrevious = closedSoFar < 2 || known < 2 ? double.NaN : expected[h][closedSoFar - 2];
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
            var sessionBars = new SessionBars();
            for (var t = 0; t < bars.Count; t++)
            {
                frame.Step(bars[t]);
                while (closedSoFar < closed.Count && closedAt[closedSoFar] <= t)
                {
                    sessionBars.Closed(closed[closedSoFar]);
                    closedSoFar++;
                }

                var minuteDate = SessionClock.DateOf(bars[t].EpochSeconds);
                for (var h = 0; h < specs.Count; h++)
                {
                    var known = SessionBars.Restarts(specs[h]) ? sessionBars.Known(minuteDate) : int.MaxValue;
                    var want = closedSoFar == 0 || known < 1 ? double.NaN : expected[h][closedSoFar - 1];
                    if (!Tolerance.Same(want, frame[h]) || frame.IsReady(h) == double.IsNaN(want))
                    {
                        Assert.Fail($"seed {seed}, with calendar, {TestSpecs.Describe(specs[h])}, step {t}: expected {want:R}, frame has {frame[h]:R}");
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(PeriodSeeds))]
    public void Weekly_and_monthly_values_equal_the_reference_over_the_weeks_and_months_closed_by_then_with_each_kind_of_calendar(int seed, CalendarKnowledge kind)
    {
        // 1,400 calendar days (46 months, 200 weeks): the longest warm-up of the variants is 42 bars, so every feature has a ready stretch on both timeframes
        var series = SeriesGenerator.Thin(seed, 1_400);
        var known = PeriodCalendars.For(series, kind);
        var specs = TestSpecs.PeriodTimeframes.SelectMany(TestSpecs.Variants).ToList();
        var frame = FeatureFrame.Create(specs, known.Options()).Value;
        var closed = TestSpecs.PeriodTimeframes.ToDictionary(t => t, t => FrameReference.ClosedByStep(series.Bars, t, known.Sessions, known.Holidays));
        var closedOfHandle = specs.Select(s => closed[s.Timeframe]).ToArray();
        var expected = specs.Select(s => ReferenceIndicators.Compute(s, [.. closed[s.Timeframe].SelectMany(x => x).Select(b => b.AsBar)])).ToList();
        var closedSoFar = new int[specs.Count];
        var readyAt = new bool[specs.Count];

        for (var t = 0; t < series.Bars.Count; t++)
        {
            frame.Step(series.Bars[t]);

            // A feature changes only when its timeframe closes a bar: every handle is compared at those steps and at every 41st; the first handle of each timeframe at all of them.
            var everything = frame.BarClosed(Timeframe.W1) || frame.BarClosed(Timeframe.Mo1) || t % 41 == 0 || t == series.Bars.Count - 1;
            for (var h = 0; h < specs.Count; h++)
            {
                closedSoFar[h] += closedOfHandle[h][t].Count;
                if (!everything && h != 0 && specs[h].Timeframe == specs[h - 1].Timeframe)
                {
                    continue;
                }

                var want = closedSoFar[h] == 0 ? double.NaN : expected[h][closedSoFar[h] - 1];
                if (!Tolerance.Same(want, frame[h]) || frame.IsReady(h) == double.IsNaN(want))
                {
                    Assert.Fail($"seed {seed}, calendar {kind}, {TestSpecs.Describe(specs[h])}, step {t}: expected {want:R}, frame has {frame[h]:R} (ready {frame.IsReady(h)})");
                }

                readyAt[h] |= frame.IsReady(h);
            }
        }

        // The comparison is not vacuous: almost every feature became ready (the pivots and a few long ones depend on the data), and weeks and months really closed.
        readyAt.Count(r => r).ShouldBeGreaterThan(specs.Count * 9 / 10, $"seed {seed}: most features were ready at some step");
        closed[Timeframe.W1].Sum(x => x.Count).ShouldBeGreaterThan(150);
        closed[Timeframe.Mo1].Sum(x => x.Count).ShouldBeGreaterThan(40);
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

    [Fact]
    public void A_weekly_feature_shows_the_last_closed_week_throughout_the_week_in_progress()
    {
        // Weeks of one-bar sessions (09:15) with closes 10 to 50, then 60 to 100, then 130 on the third Monday: the week in progress is never in the feature.
        var week1 = BarFixtures.Mon;
        var week2 = BarFixtures.Mon.AddDays(7);
        var week3 = BarFixtures.Mon.AddDays(14);
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.W1, PriceSource.Close, 1), new FeatureSpec("sma", Timeframe.W1, PriceSource.Close, 2), new FeatureSpec("highest", Timeframe.W1, PriceSource.High, 2)]).Value;
        var shown = new List<(DateOnly Date, double One, double Two, double Highest)>();

        for (var d = 0; d < 15; d++)
        {
            var date = week1.AddDays(d);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var close = 10.0 * (d < 7 ? d + 1 : d - 1);
            frame.Step(Minutes.Bar(date, Minutes.Open, close, close + 1, close - 1, close));
            shown.Add((date, frame[0], frame[1], frame[2]));
        }

        // Monday to Friday of week 1 (closes 10 to 50): nothing is closed until the first bar of week 2 closes week 1 with its last close, 50, and its highest high, 51
        shown.Where(x => x.Date < week2).ShouldAllBe(x => double.IsNaN(x.One), "week 1 is the week in progress");
        var mondayTwo = shown.Single(x => x.Date == week2);
        mondayTwo.One.ShouldBe(50.0);
        mondayTwo.Two.ShouldBeNaN("one week has closed");
        mondayTwo.Highest.ShouldBeNaN("one week has closed");
        shown.Where(x => x.Date > week2 && x.Date < week3).ShouldAllBe(x => x.One == 50.0, "week 2 is the week in progress: the feature stays at the close of week 1 on every day of it");

        // the first bar of week 3 closes week 2 (closes 60 to 100, high 101)
        var mondayThree = shown.Single(x => x.Date == week3);
        (mondayThree.One, mondayThree.Two, mondayThree.Highest).ShouldBe((100.0, 75.0, 101.0));
    }

    [Fact]
    public void A_monthly_feature_shows_the_last_closed_month_and_a_calendar_closes_it_on_its_last_session()
    {
        // Friday 27 February 2026 is the last session of February; the first bars of March must not be in the February bar
        var bars = new List<Bar>();
        foreach (var (date, close) in new[] { (new DateOnly(2026, 2, 25), 100.0), (new DateOnly(2026, 2, 26), 110.0), (new DateOnly(2026, 2, 27), 120.0), (new DateOnly(2026, 3, 2), 900.0), (new DateOnly(2026, 3, 3), 950.0) })
        {
            bars.AddRange(BarFixtures.Brief(date, close, close + 5, close - 5, close, 1_000));
        }

        var withCalendar = FeatureFrame.Create([new FeatureSpec("close", Timeframe.Mo1), new FeatureSpec("high", Timeframe.Mo1), new FeatureSpec("volume", Timeframe.Mo1)], new FrameOptions(new XDrishti.Domain.Market.Calendar.TradingCalendar())).Value;
        var without = FeatureFrame.Create([new FeatureSpec("close", Timeframe.Mo1), new FeatureSpec("high", Timeframe.Mo1), new FeatureSpec("volume", Timeframe.Mo1)]).Value;
        var withValues = new List<double>();
        var withoutValues = new List<double>();

        foreach (var bar in bars)
        {
            withCalendar.Step(bar);
            without.Step(bar);
            withValues.Add(withCalendar[0]);
            withoutValues.Add(without[0]);
        }

        // 2 bars per day: the 15:29 bar of the 27th is the 6th bar (index 5)
        withValues.Take(5).ShouldAllBe(v => double.IsNaN(v));
        withValues.Skip(5).ShouldAllBe(v => v == 120.0, "closed by the 15:29 bar of Friday the 27th, the last weekday of February");
        withoutValues.Take(6).ShouldAllBe(v => double.IsNaN(v));
        withoutValues.Skip(6).ShouldAllBe(v => v == 120.0, "closed by the first bar of March");
        (withCalendar[1], withCalendar[2]).ShouldBe((125.0, 3_000.0), "February's highest high and its three days of 1,000");
    }
}
