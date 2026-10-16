using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>BarClosed, ClosedBarsAt and TryLastClosed against the independent grouping reference (criterion 1 of P3-01b).</summary>
public sealed class FrameClosedFlagsTests
{
    private static readonly Timeframe[] Asked = [Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.D1];

    public static TheoryData<string, bool> Fixtures
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var name in BarFixtures.Names)
            {
                data.Add(name, false);
                data.Add(name, true);
            }

            return data;
        }
    }

    public static TheoryData<int, bool> Seeds
    {
        get
        {
            var data = new TheoryData<int, bool>();
            foreach (var seed in Enumerable.Range(1, 30))
            {
                data.Add(seed, seed % 2 == 0);
            }

            return data;
        }
    }

    private static readonly Timeframe[] AskedWithPeriods = [Timeframe.M1, Timeframe.H1, Timeframe.D1, Timeframe.W1, Timeframe.Mo1];

    public static TheoryData<string, CalendarKnowledge> PeriodFixtures
    {
        get
        {
            var data = new TheoryData<string, CalendarKnowledge>();
            foreach (var name in BarFixtures.PeriodNames)
            {
                foreach (var kind in Enum.GetValues<CalendarKnowledge>())
                {
                    data.Add(name, kind);
                }
            }

            return data;
        }
    }

    public static TheoryData<int, CalendarKnowledge> PeriodSeeds
    {
        get
        {
            var data = new TheoryData<int, CalendarKnowledge>();
            foreach (var seed in Enumerable.Range(1, 40))
            {
                data.Add(seed, (CalendarKnowledge)(seed % 4));
            }

            return data;
        }
    }

    private static FeatureFrame Frame(bool calendar, List<Bar> bars, params Timeframe[] timeframes) =>
        FeatureFrame.Create([.. timeframes.Select(t => new FeatureSpec("sma", t, PriceSource.Close, 1))], calendar ? new FrameOptions(CalendarFixtures.For(bars)) : null).Value;

    private static void ShouldAgree(List<Bar> bars, bool calendar, string label) =>
        ShouldAgree(bars, Asked, calendar ? new FrameOptions(CalendarFixtures.For(bars)) : null, calendar ? CalendarFixtures.Windows(bars) : null, null, label);

    private static void ShouldAgree(List<Bar> bars, Timeframe[] asked, FrameOptions? options, IReadOnlyDictionary<DateOnly, (int From, int To)>? sessions, IReadOnlySet<DateOnly>? holidays, string label)
    {
        var frame = FeatureFrame.Create([.. asked.Select(t => new FeatureSpec("sma", t, PriceSource.Close, 1))], options).Value;
        var expected = asked.ToDictionary(t => t, t => FrameReference.ClosedByStep(bars, t, sessions, holidays));
        var last = asked.ToDictionary(t => t, _ => (ClosedBar?)null);

        foreach (var timeframe in asked)
        {
            frame.BarClosed(timeframe).ShouldBeFalse(label + ": nothing closed before the first step");
            frame.ClosedBarsAt(timeframe).ShouldBe(0);
            frame.TryLastClosed(timeframe, out _).ShouldBeFalse();
        }

        for (var t = 0; t < bars.Count; t++)
        {
            frame.Step(bars[t]);
            foreach (var timeframe in asked)
            {
                var closed = expected[timeframe][t];
                if (closed.Count > 0)
                {
                    var want = closed[^1];
                    last[timeframe] = new ClosedBar(want.Start, want.AsBar, want.Bars, want.Complete, timeframe);
                }

                var any = frame.TryLastClosed(timeframe, out var bar);
                if (frame.ClosedBarsAt(timeframe) != closed.Count || frame.BarClosed(timeframe) != closed.Count > 0 || any != last[timeframe].HasValue || (any && bar != last[timeframe]))
                {
                    Assert.Fail($"{label}, {timeframe.ToCode()}, step {t}: closed {frame.ClosedBarsAt(timeframe)} (flag {frame.BarClosed(timeframe)}), last {(any ? bar.ToString() : "none")}; the reference closed {closed.Count}, last {last[timeframe]?.ToString() ?? "none"}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_flags_and_the_last_closed_bar_match_the_reference_on_every_fixture(string fixture, bool calendar) =>
        ShouldAgree(BarFixtures.Get(fixture), calendar, fixture + (calendar ? " with calendar" : string.Empty));

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_flags_and_the_last_closed_bar_match_the_reference_on_random_series_with_special_sessions(int seed, bool calendar) =>
        ShouldAgree(SeriesGenerator.Random(seed, 600, 2_500, specialSessions: true), calendar, $"seed {seed}" + (calendar ? " with calendar" : string.Empty));

    [Theory]
    [MemberData(nameof(PeriodFixtures))]
    public void The_weekly_and_monthly_flags_and_last_closed_bar_match_the_reference_on_every_period_fixture_with_each_kind_of_calendar(string fixture, CalendarKnowledge kind)
    {
        var series = BarFixtures.GetPeriods(fixture);
        var known = PeriodCalendars.For(series, kind);

        ShouldAgree(series.Bars, AskedWithPeriods, known.Options(), known.Sessions, known.Holidays, $"{fixture}, calendar {kind}");
    }

    [Theory]
    [MemberData(nameof(PeriodSeeds))]
    public void The_weekly_and_monthly_flags_and_last_closed_bar_match_the_reference_on_long_random_series(int seed, CalendarKnowledge kind)
    {
        var series = SeriesGenerator.Thin(seed, 420);
        var known = PeriodCalendars.For(series, kind);

        ShouldAgree(series.Bars, AskedWithPeriods, known.Options(), known.Sessions, known.Holidays, $"seed {seed}, calendar {kind}");
    }

    [Fact]
    public void A_holiday_on_the_friday_closes_the_week_at_the_thursday_close_with_a_calendar_and_at_the_next_monday_without()
    {
        var series = BarFixtures.GetPeriods("holiday friday");
        var thursdayClose = series.Bars.FindIndex(b => b.EpochSeconds == Minutes.Epoch(BarFixtures.Thu, Minutes.At(15, 29)));
        thursdayClose.ShouldBe(1499);
        var withCalendar = FeatureFrame.Create([new FeatureSpec("close", Timeframe.W1)], PeriodCalendars.For(series, CalendarKnowledge.Full).Options()).Value;
        var without = FeatureFrame.Create([new FeatureSpec("close", Timeframe.W1)]).Value;
        var closedWith = new List<int>();
        var closedWithout = new List<int>();

        for (var t = 0; t < 1876; t++)
        {
            withCalendar.Step(series.Bars[t]);
            without.Step(series.Bars[t]);
            if (withCalendar.BarClosed(Timeframe.W1))
            {
                closedWith.Add(t);
            }

            if (without.BarClosed(Timeframe.W1))
            {
                closedWithout.Add(t);
            }
        }

        closedWith.ShouldBe([thursdayClose]);
        closedWithout.ShouldBe([thursdayClose + 1], "the Monday that follows: the first bar of the next week");
        foreach (var frame in new[] { withCalendar, without })
        {
            frame.TryLastClosed(Timeframe.W1, out var week).ShouldBeTrue();
            week.StartEpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Mon, 0));
            (week.Bars, week.Complete, week.Timeframe).ShouldBe((1500, true, Timeframe.W1));
            week.Bar.Open.ShouldBe(series.Bars[0].Open);
            week.Bar.Close.ShouldBe(series.Bars[thursdayClose].Close);
            week.Bar.High.ShouldBe(series.Bars.Take(1500).Max(b => b.High));
            week.Bar.Low.ShouldBe(series.Bars.Take(1500).Min(b => b.Low));
            week.Bar.Volume.ShouldBe(series.Bars.Take(1500).Sum(b => b.Volume));
        }
    }

    [Fact]
    public void The_week_and_the_month_and_the_day_close_at_the_same_step_when_a_year_ends_on_the_thursday_before_a_holiday()
    {
        var series = BarFixtures.GetPeriods("year end");
        var frame = FeatureFrame.Create(
            [new FeatureSpec("close", Timeframe.D1), new FeatureSpec("close", Timeframe.W1), new FeatureSpec("close", Timeframe.Mo1)],
            PeriodCalendars.For(series, CalendarKnowledge.Full).Options()).Value;
        var step = series.Bars.FindIndex(b => b.EpochSeconds == Minutes.Epoch(new DateOnly(2026, 12, 31), Minutes.At(15, 29)));

        for (var t = 0; t <= step; t++)
        {
            frame.Step(series.Bars[t]);
            if (t < step)
            {
                frame.BarClosed(Timeframe.W1).ShouldBeFalse($"step {t}");
                frame.BarClosed(Timeframe.Mo1).ShouldBeFalse($"step {t}");
            }
        }

        step.ShouldBe(1499);
        foreach (var timeframe in new[] { Timeframe.D1, Timeframe.W1, Timeframe.Mo1 })
        {
            frame.ClosedBarsAt(timeframe).ShouldBe(1, timeframe.ToCode());
            frame.TryLastClosed(timeframe, out var closed).ShouldBeTrue();
            closed.Bar.Close.ShouldBe(series.Bars[step].Close, timeframe.ToCode());
        }

        frame.TryLastClosed(Timeframe.W1, out var week).ShouldBeTrue();
        frame.TryLastClosed(Timeframe.Mo1, out var month).ShouldBeTrue();
        SessionClock.DateOf(week.StartEpochSeconds).ShouldBe(new DateOnly(2026, 12, 28), "the week holds the last days of two years");
        SessionClock.DateOf(month.StartEpochSeconds).ShouldBe(new DateOnly(2026, 12, 1));
    }

    [Fact]
    public void One_minute_can_close_a_late_week_and_the_one_session_week_that_begins_with_it_and_the_frame_reports_two()
    {
        // Friday has no bars but the calendar says it is a session: Thursday's close does not end the week. The next Monday is a one-minute special session and the only session of its week.
        var nextMonday = BarFixtures.Mon.AddDays(7);
        List<Bar> bars = [.. BarFixtures.Walk(BarFixtures.Mon, Minutes.Open, 375), .. BarFixtures.Walk(BarFixtures.Tue, Minutes.Open, 375), .. BarFixtures.Walk(BarFixtures.Wed, Minutes.Open, 375), .. BarFixtures.Walk(BarFixtures.Thu, Minutes.Open, 375), .. BarFixtures.Walk(nextMonday, Minutes.Open, 1)];
        var calendar = CalendarFixtures.For(new Dictionary<DateOnly, (int From, int To)> { [nextMonday] = (Minutes.Open, Minutes.Open + 1) }, [.. Enumerable.Range(1, 4).Select(d => nextMonday.AddDays(d))]);
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.W1), new FeatureSpec("sma", Timeframe.W1, PriceSource.Close, 2)], new FrameOptions(calendar)).Value;

        foreach (var bar in bars.Take(1500))
        {
            frame.Step(bar);
            frame.BarClosed(Timeframe.W1).ShouldBeFalse("Thursday's close does not end the week while Friday is a session");
        }

        frame.Step(bars[^1]);

        frame.ClosedBarsAt(Timeframe.W1).ShouldBe(2);
        frame.BarClosed(Timeframe.W1).ShouldBeTrue();
        frame.TryLastClosed(Timeframe.W1, out var last).ShouldBeTrue();
        (last.StartEpochSeconds, last.Bars, last.Complete).ShouldBe((Minutes.Epoch(nextMonday, 0), 1, false), "the second bar closed is the new one-session week");
        frame[0].ShouldBe(bars[^1].Close);
        frame.At(0, 1).ShouldBe(bars[1499].Close, "the late week, closed first, is one lag back");
        frame.IncompleteBarsConsumed.ShouldBe(1);
        frame[1].ShouldBe((bars[1499].Close + bars[^1].Close) / 2, 1e-9);
    }

    [Fact]
    public void The_weekly_flags_describe_the_last_step_only_and_the_last_closed_week_stays_until_the_next_one_closes()
    {
        var series = BarFixtures.GetPeriods("holiday friday");
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.W1)], PeriodCalendars.For(series, CalendarKnowledge.Full).Options()).Value;
        for (var t = 0; t < 1500; t++)
        {
            frame.Step(series.Bars[t]);
        }

        frame.BarClosed(Timeframe.W1).ShouldBeTrue();
        frame.TryLastClosed(Timeframe.W1, out var first).ShouldBeTrue();

        for (var t = 1500; t < 3374; t++)
        {
            frame.Step(series.Bars[t]);
            frame.BarClosed(Timeframe.W1).ShouldBeFalse($"step {t}");
            frame.ClosedBarsAt(Timeframe.W1).ShouldBe(0);
            frame.TryLastClosed(Timeframe.W1, out var still).ShouldBeTrue();
            still.ShouldBe(first, $"step {t}: the next week has not closed");
        }

        frame.Step(series.Bars[3374]);
        frame.BarClosed(Timeframe.W1).ShouldBeTrue("Friday 13 March 15:29: the second week of the fixture");
        frame.TryLastClosed(Timeframe.W1, out var second).ShouldBeTrue();
        SessionClock.DateOf(second.StartEpochSeconds).ShouldBe(BarFixtures.Mon.AddDays(7));
    }

    [Fact]
    public void One_minute_closes_a_bar_at_every_accepted_step()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 3)]).Value;

        foreach (var bar in Minutes.Closes(BarFixtures.Mon, 1, 2, 3, 4))
        {
            frame.Step(bar);
            frame.BarClosed(Timeframe.M1).ShouldBeTrue();
            frame.ClosedBarsAt(Timeframe.M1).ShouldBe(1);
            frame.TryLastClosed(Timeframe.M1, out var last).ShouldBeTrue();
            (last.Bar, last.Bars, last.Complete, last.Timeframe).ShouldBe((bar, 1, true, Timeframe.M1));
        }
    }

    [Fact]
    public void A_minute_that_closes_two_buckets_reports_two_and_the_second_is_the_last_closed()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M5, PriceSource.Close, 1)]).Value;
        frame.Step(Minutes.Bar(BarFixtures.Mon, Minutes.Open, 10, 12, 9, 11, 5));

        // 09:24 is the only bar of the 09:20 bucket and its last minute: it also proves the 09:15 bucket is over
        frame.Step(Minutes.Bar(BarFixtures.Mon, Minutes.Open + 9, 20, 22, 19, 21, 7));

        frame.ClosedBarsAt(Timeframe.M5).ShouldBe(2);
        frame.BarClosed(Timeframe.M5).ShouldBeTrue();
        frame.TryLastClosed(Timeframe.M5, out var last).ShouldBeTrue();
        last.StartEpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Mon, Minutes.Open + 5));
        (last.Bars, last.Complete).ShouldBe((1, false));
        frame.IncompleteBarsConsumed.ShouldBe(2);
    }

    [Fact]
    public void A_late_closed_incomplete_bucket_is_reported_by_the_bar_that_proves_it_over_with_the_incomplete_flag()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M5, PriceSource.Close, 1)]).Value;
        for (var m = Minutes.Open; m < Minutes.Open + 4; m++)
        {
            frame.Step(Minutes.Close(BarFixtures.Mon, m, m));
            frame.BarClosed(Timeframe.M5).ShouldBeFalse();
        }

        // 09:19 never comes; 09:20 starts the next bucket and closes the previous one
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 5, 1));

        frame.BarClosed(Timeframe.M5).ShouldBeTrue();
        frame.TryLastClosed(Timeframe.M5, out var last).ShouldBeTrue();
        (last.Bars, last.Complete).ShouldBe((4, false));
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 6, 1));
        frame.BarClosed(Timeframe.M5).ShouldBeFalse();
        frame.TryLastClosed(Timeframe.M5, out var still).ShouldBeTrue();
        still.ShouldBe(last, "the last closed bar stays until the next one closes");
    }

    [Fact]
    public void A_special_session_closes_its_day_at_its_own_last_minute_only_when_the_frame_has_the_calendar()
    {
        var bars = BarFixtures.Get("saturday session");
        var withCalendar = Frame(true, bars, Timeframe.D1);
        var without = Frame(false, bars, Timeframe.D1);

        foreach (var bar in bars)
        {
            withCalendar.Step(bar);
            without.Step(bar);
        }

        withCalendar.BarClosed(Timeframe.D1).ShouldBeTrue();
        without.BarClosed(Timeframe.D1).ShouldBeFalse();
        without.TryLastClosed(Timeframe.D1, out _).ShouldBeFalse();
        withCalendar.TryLastClosed(Timeframe.D1, out var day).ShouldBeTrue();
        (day.Bars, day.Complete).ShouldBe((30, false));

        // the first bar of a later session closes it for the frame without the calendar
        without.Step(BarFixtures.Walk(new DateOnly(2026, 3, 9), Minutes.Open, 1)[0]);
        without.BarClosed(Timeframe.D1).ShouldBeTrue();
    }

    [Fact]
    public void Asking_for_a_timeframe_the_frame_was_not_built_for_is_an_error_not_a_silent_false()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M5, PriceSource.Close, 3), new FeatureSpec("sma", Timeframe.D1, PriceSource.Close, 3)]).Value;

        frame.HasTimeframe(Timeframe.M5).ShouldBeTrue();
        frame.HasTimeframe(Timeframe.D1).ShouldBeTrue();
        foreach (var timeframe in new[] { Timeframe.M1, Timeframe.M15, Timeframe.H1, Timeframe.W1, Timeframe.Mo1, (Timeframe)99 })
        {
            frame.HasTimeframe(timeframe).ShouldBeFalse();
            Should.Throw<ArgumentOutOfRangeException>(() => frame.BarClosed(timeframe));
            Should.Throw<ArgumentOutOfRangeException>(() => frame.ClosedBarsAt(timeframe));
            Should.Throw<ArgumentOutOfRangeException>(() => frame.TryLastClosed(timeframe, out _));
        }
    }

    [Fact]
    public void A_bar_the_ordering_guard_skips_closes_nothing_on_any_timeframe()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 2), new FeatureSpec("sma", Timeframe.M5, PriceSource.Close, 2)]).Value;
        var bars = Minutes.Closes(BarFixtures.Mon, 1, 2, 3, 4, 5);
        foreach (var bar in bars)
        {
            frame.Step(bar);
        }

        frame.BarClosed(Timeframe.M5).ShouldBeTrue();

        frame.Step(bars[^1]);

        frame.BarClosed(Timeframe.M1).ShouldBeFalse();
        frame.BarClosed(Timeframe.M5).ShouldBeFalse();
        frame.ClosedBarsAt(Timeframe.M1).ShouldBe(0);
        frame.IgnoredBars.ShouldBe(1);
    }

    [Fact]
    public void Reset_forgets_the_flags_and_the_last_closed_bar()
    {
        var frame = Frame(false, [], Timeframe.M1, Timeframe.M5);
        foreach (var bar in BarFixtures.Get("normal day").Take(10))
        {
            frame.Step(bar);
        }

        frame.Reset();

        frame.BarClosed(Timeframe.M1).ShouldBeFalse();
        frame.BarClosed(Timeframe.M5).ShouldBeFalse();
        frame.TryLastClosed(Timeframe.M5, out _).ShouldBeFalse();
        frame.IgnoredBars.ShouldBe(0);
    }

    [Fact]
    public void Reset_forgets_the_weekly_and_monthly_flags_the_last_closed_week_and_the_open_one()
    {
        var series = BarFixtures.GetPeriods("year end");
        var options = PeriodCalendars.For(series, CalendarKnowledge.Full).Options();
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.W1), new FeatureSpec("close", Timeframe.Mo1)], options).Value;
        for (var t = 0; t < 1500; t++)
        {
            frame.Step(series.Bars[t]);
        }

        frame.BarClosed(Timeframe.W1).ShouldBeTrue();
        frame.Reset();

        frame.BarClosed(Timeframe.W1).ShouldBeFalse();
        frame.BarClosed(Timeframe.Mo1).ShouldBeFalse();
        frame.ClosedBarsAt(Timeframe.Mo1).ShouldBe(0);
        frame.TryLastClosed(Timeframe.W1, out _).ShouldBeFalse();
        frame.TryLastClosed(Timeframe.Mo1, out _).ShouldBeFalse();

        // the open week of the first run is forgotten: the second run closes the same week with exactly the minutes it was fed
        for (var t = 100; t < 1500; t++)
        {
            frame.Step(series.Bars[t]);
        }

        frame.TryLastClosed(Timeframe.W1, out var week).ShouldBeTrue();
        week.Bars.ShouldBe(1400);
        week.Bar.Open.ShouldBe(series.Bars[100].Open);
    }
}
