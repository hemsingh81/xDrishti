using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// The weekly and monthly timeframes of the builder (P3-01c): the buckets against an independent grouping of the 1-minute bars by Monday-anchored week and calendar month, with a calendar, with one that does not know the
/// weekend sessions, and without; the step that closes a period (the close of the last session with a calendar, the first bar of the next period without, never earlier); the bars of an emitted period that are ignored
/// and counted; hand-calculated weeks and months; ordering, reset, determinism and the prefix property.
/// </summary>
public sealed class WeeklyMonthlyBuilderTests
{
    private static readonly Timeframe[] PeriodFrames = [Timeframe.W1, Timeframe.Mo1];

    private static readonly CalendarKnowledge[] Kinds = [CalendarKnowledge.None, CalendarKnowledge.Full, CalendarKnowledge.WeekdaysOnly, CalendarKnowledge.HolidaysOnly];

    private static readonly DateOnly NextMon = BarFixtures.Mon.AddDays(7);

    public static TheoryData<string, Timeframe, CalendarKnowledge> NamedCases
    {
        get
        {
            var data = new TheoryData<string, Timeframe, CalendarKnowledge>();
            foreach (var name in BarFixtures.PeriodNames)
            {
                foreach (var timeframe in PeriodFrames)
                {
                    foreach (var kind in Kinds)
                    {
                        data.Add(name, timeframe, kind);
                    }
                }
            }

            return data;
        }
    }

    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 40)];

    public static TheoryData<int> PrefixSeeds { get; } = [.. Enumerable.Range(1, 200)];

    private static List<(int Step, ClosedBar Closed)> Run(IReadOnlyList<Bar> bars, Timeframe timeframe, TradingCalendar? calendar = null) =>
        BuilderRun.Run(bars, timeframe, new TimeframeBuilder(timeframe, calendar));

    private static List<(int Step, DateOnly Start, int Bars, bool Complete)> Summary(IEnumerable<(int Step, ClosedBar Closed)> run) =>
        [.. run.Select(x => (x.Step, SessionClock.DateOf(x.Closed.StartEpochSeconds), x.Closed.Bars, x.Closed.Complete))];

    private static void ShouldMatchReference(PeriodSeries series, Timeframe timeframe, CalendarKnowledge kind, string label)
    {
        var known = PeriodCalendars.For(series, kind);
        var periods = ReferenceBuckets.BuildPeriods(series.Bars, timeframe, known.Reference);
        var expected = periods.Where(p => p.Bucket.CloseIndex != int.MaxValue).Select(p => p.Bucket).OrderBy(b => b.CloseIndex).ThenBy(b => b.Start).ToList();
        var (actual, ignored) = BuilderRun.RunCounting(series.Bars, timeframe, known.Calendar);

        actual.Count.ShouldBe(expected.Count, label);
        for (var i = 0; i < expected.Count; i++)
        {
            var (step, closed) = actual[i];
            var want = expected[i];
            var where = $"{label}, {timeframe.ToCode()} period {i} at {DateTimeOffset.FromUnixTimeSeconds(want.Start):u}";
            step.ShouldBe(want.CloseIndex, where);
            closed.StartEpochSeconds.ShouldBe(want.Start, where);
            closed.Bar.EpochSeconds.ShouldBe(want.Start, where);
            closed.Timeframe.ShouldBe(timeframe, where);
            closed.Bar.Open.ShouldBe(want.Open, where);
            closed.Bar.High.ShouldBe(want.High, where);
            closed.Bar.Low.ShouldBe(want.Low, where);
            closed.Bar.Close.ShouldBe(want.Close, where);
            closed.Bar.Volume.ShouldBe(want.Volume, where);
            closed.Bars.ShouldBe(want.Bars, where);
            closed.Complete.ShouldBe(want.Complete, where);
        }

        // Every minute pushed is in an emitted period, in the period still open, or was ignored because its period was over.
        ignored.ShouldBe(periods.Sum(p => (long)p.Excluded), label);
        var open = periods.Where(p => p.Bucket.CloseIndex == int.MaxValue).Sum(p => p.Bucket.Bars);
        (actual.Sum(x => x.Closed.Bars) + open + (int)ignored).ShouldBe(series.Bars.Count, label);
    }

    [Fact]
    public void The_fixture_dates_fall_on_the_weekdays_the_tests_assume()
    {
        BarFixtures.Mon.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        BarFixtures.Thu.DayOfWeek.ShouldBe(DayOfWeek.Thursday);
        BarFixtures.Fri.DayOfWeek.ShouldBe(DayOfWeek.Friday);
        new DateOnly(2026, 1, 31).DayOfWeek.ShouldBe(DayOfWeek.Saturday);
        new DateOnly(2026, 11, 8).DayOfWeek.ShouldBe(DayOfWeek.Sunday);
        new DateOnly(2026, 12, 31).DayOfWeek.ShouldBe(DayOfWeek.Thursday);
        new DateOnly(2028, 2, 29).DayOfWeek.ShouldBe(DayOfWeek.Tuesday);
        new DateOnly(2027, 2, 28).DayOfWeek.ShouldBe(DayOfWeek.Sunday);
    }

    [Fact]
    public void The_random_series_contain_the_cases_the_reference_tests_are_meant_to_cover()
    {
        var series = Enumerable.Range(1, 40).Select(seed => BarFixtures.RandomPeriods(seed, 90)).ToList();
        var dates = series.Select(s => s.Bars.GroupBy(b => SessionClock.DateOf(b.EpochSeconds)).ToList()).ToList();

        series.Sum(s => s.Holidays.Count).ShouldBeGreaterThan(100);
        dates.SelectMany(d => d).Count(d => d.Key.DayOfWeek == DayOfWeek.Saturday).ShouldBeGreaterThan(20);
        dates.SelectMany(d => d).Count(d => d.Key.DayOfWeek == DayOfWeek.Sunday).ShouldBeGreaterThan(3);
        dates.SelectMany(d => d).Count(d => d.Count() < 375 && d.Key.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ShouldBeGreaterThan(150);
        series.Count(s => s.Holidays.Any(h => h.DayOfWeek == DayOfWeek.Friday)).ShouldBeGreaterThan(10);
        series.Sum(s => ReferenceBuckets.BuildPeriods(s.Bars, Timeframe.W1, new ReferenceCalendar(CalendarFixtures.WeekdayWindows(s.Bars), s.Holidays)).Sum(p => p.Excluded)).ShouldBeGreaterThan(100, "unknown weekend sessions exist");
    }

    [Theory]
    [MemberData(nameof(NamedCases))]
    public void Periods_equal_the_grouping_reference_on_every_fixture(string fixture, Timeframe timeframe, CalendarKnowledge kind) =>
        ShouldMatchReference(BarFixtures.GetPeriods(fixture), timeframe, kind, $"{fixture}, calendar {kind}");

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Periods_equal_the_grouping_reference_on_random_multi_week_and_multi_month_series(int seed)
    {
        var series = BarFixtures.RandomPeriods(seed, 70 + seed * 2);

        foreach (var timeframe in PeriodFrames)
        {
            foreach (var kind in Kinds)
            {
                ShouldMatchReference(series, timeframe, kind, $"seed {seed}, calendar {kind}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Without_a_calendar_a_period_is_never_emitted_before_the_calendar_emits_it_and_never_later_than_the_first_bar_of_the_next_period(int seed)
    {
        var series = BarFixtures.RandomPeriods(seed, 80);
        var calendar = CalendarFixtures.For(series.Bars, series.Holidays);

        foreach (var timeframe in PeriodFrames)
        {
            var with = Run(series.Bars, timeframe, calendar).ToDictionary(x => x.Closed.StartEpochSeconds, x => x.Step);
            var without = Run(series.Bars, timeframe);

            foreach (var (step, closed) in without)
            {
                var start = SessionClock.DateOf(closed.StartEpochSeconds);
                var end = timeframe == Timeframe.W1 ? start.AddDays(7) : start.AddMonths(1);
                var where = $"seed {seed}, {timeframe.ToCode()}, period {start}";

                SessionClock.DateOf(series.Bars[step].EpochSeconds).ShouldBeGreaterThanOrEqualTo(end, where + ": the bar that emits it is the first of a later period");
                SessionClock.DateOf(series.Bars[step - 1].EpochSeconds).ShouldBeLessThan(end, where + ": the bar before it is still in the period");
                if (with.TryGetValue(closed.StartEpochSeconds, out var earlier))
                {
                    earlier.ShouldBeLessThanOrEqualTo(step, where);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void A_week_or_month_is_the_sum_of_the_days_the_1D_lane_gives_for_it(int seed)
    {
        var series = BarFixtures.RandomPeriods(seed, 70);

        foreach (var kind in new[] { CalendarKnowledge.None, CalendarKnowledge.Full })
        {
            var calendar = PeriodCalendars.For(series, kind).Calendar;
            var days = Run(series.Bars, Timeframe.D1, calendar).Select(x => x.Closed).ToList();
            foreach (var timeframe in PeriodFrames)
            {
                foreach (var (_, period) in Run(series.Bars, timeframe, calendar))
                {
                    var start = SessionClock.DateOf(period.StartEpochSeconds);
                    var inside = days.Where(d => timeframe == Timeframe.W1
                        ? SessionClock.DateOf(d.StartEpochSeconds) >= start && SessionClock.DateOf(d.StartEpochSeconds) < start.AddDays(7)
                        : SessionClock.DateOf(d.StartEpochSeconds).Year == start.Year && SessionClock.DateOf(d.StartEpochSeconds).Month == start.Month).ToList();
                    var where = $"seed {seed}, calendar {kind}, {timeframe.ToCode()} {start}";

                    period.Bar.Open.ShouldBe(inside[0].Bar.Open, where);
                    period.Bar.High.ShouldBe(inside.Max(d => d.Bar.High), where);
                    period.Bar.Low.ShouldBe(inside.Min(d => d.Bar.Low), where);
                    period.Bar.Close.ShouldBe(inside[^1].Bar.Close, where);
                    period.Bar.Volume.ShouldBe(inside.Sum(d => d.Bar.Volume), where);
                    period.Bars.ShouldBe(inside.Sum(d => d.Bars), where);
                    period.Complete.ShouldBe(inside.All(d => d.Complete), where);
                }
            }
        }
    }

    [Fact]
    public void A_week_takes_its_open_from_its_first_day_its_extremes_from_all_days_and_its_close_from_its_last_day()
    {
        List<Bar> bars =
        [
            .. BarFixtures.Brief(BarFixtures.Mon, 100, 110, 95, 105, 1_000),
            .. BarFixtures.Brief(BarFixtures.Tue, 105, 120, 101, 118, 2_000),
            .. BarFixtures.Brief(BarFixtures.Wed, 118, 119, 90, 92, 3_000),
            .. BarFixtures.Brief(NextMon, 92, 93, 91, 92, 5),
        ];

        var week = Run(bars, Timeframe.W1).Single();

        week.Step.ShouldBe(6, "the first bar of the next week");
        week.Closed.StartEpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Mon, 0));
        week.Closed.Bar.ShouldBe(new Bar(Minutes.Epoch(BarFixtures.Mon, 0), 100, 120, 90, 92, 6_000));
        (week.Closed.Bars, week.Closed.Complete, week.Closed.Timeframe).ShouldBe((6, false, Timeframe.W1));
    }

    [Fact]
    public void A_month_starts_on_the_first_even_when_it_is_a_sunday_and_ends_on_its_last_session()
    {
        // Friday 30 January closes January 2026 (the 31st is a Saturday). Sunday 1 February is no session, but it is where February starts.
        List<Bar> bars =
        [
            .. BarFixtures.Brief(new DateOnly(2026, 1, 29), 50, 60, 40, 55, 100),
            .. BarFixtures.Brief(new DateOnly(2026, 1, 30), 55, 80, 52, 70, 200),
            .. BarFixtures.Brief(new DateOnly(2026, 2, 2), 70, 71, 69, 70, 7),
        ];
        var calendar = CalendarFixtures.For(bars, []);

        var withCalendar = Run(bars, Timeframe.Mo1, calendar).Single();
        var without = Run(bars, Timeframe.Mo1).Single();

        withCalendar.Step.ShouldBe(3, "the 15:29 bar of Friday the 30th");
        without.Step.ShouldBe(4, "the first bar of February");
        foreach (var month in new[] { withCalendar.Closed, without.Closed })
        {
            month.StartEpochSeconds.ShouldBe(Minutes.Epoch(new DateOnly(2026, 1, 1), 0));
            month.Bar.ShouldBe(new Bar(Minutes.Epoch(new DateOnly(2026, 1, 1), 0), 50, 80, 40, 70, 300));
            (month.Bars, month.Timeframe).ShouldBe((4, Timeframe.Mo1));
        }

        var february = Run([.. bars, .. BarFixtures.Brief(new DateOnly(2026, 3, 2), 70, 71, 69, 70, 1)], Timeframe.Mo1).Last();
        february.Closed.StartEpochSeconds.ShouldBe(Minutes.Epoch(new DateOnly(2026, 2, 1), 0), "the 1st of February is a Sunday: the month still starts at its midnight");
    }

    [Fact]
    public void A_week_or_month_is_complete_when_every_day_that_has_bars_is_and_the_running_period_is_not_incomplete_for_being_unfinished()
    {
        var half = BarFixtures.GetPeriods("half missing day");
        var clean = BarFixtures.GetPeriods("holiday friday");

        var halfWeeks = Summary(Run(half.Bars, Timeframe.W1));
        var cleanWeeks = Summary(Run(clean.Bars, Timeframe.W1));

        halfWeeks.Select(w => w.Complete).ShouldBe([false, true], "a Wednesday with every second minute missing makes its week incomplete, the next week is clean");
        cleanWeeks.Select(w => w.Complete).ShouldBe([true, true], "four sessions in a week with a holiday are all of its sessions");
        halfWeeks[0].Bars.ShouldBe(4 * 375 + 188, "the odd minutes of Wednesday's 375 are the 188 that are there");
    }

    [Fact]
    public void With_a_holiday_on_the_friday_the_week_is_emitted_at_the_thursday_close_and_without_a_calendar_at_the_first_bar_of_the_next_week()
    {
        var series = BarFixtures.GetPeriods("holiday friday");
        series.Bars[1499].EpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Thu, Minutes.At(15, 29)));
        series.Bars[1500].EpochSeconds.ShouldBe(Minutes.Epoch(NextMon, Minutes.Open));

        var withCalendar = Summary(Run(series.Bars, Timeframe.W1, CalendarFixtures.For(series.Bars, series.Holidays)));
        var without = Summary(Run(series.Bars, Timeframe.W1));
        var friday = Summary(Run(series.Bars, Timeframe.W1, CalendarFixtures.For(series.Bars, [])));

        withCalendar.ShouldBe([(1499, BarFixtures.Mon, 1500, true), (3374, NextMon, 1875, true)]);
        without.ShouldBe([(1500, BarFixtures.Mon, 1500, true), (3375, NextMon, 1875, true)]);
        friday.ShouldBe([(1500, BarFixtures.Mon, 1500, true), (3374, NextMon, 1875, true)], "a calendar that does not know the holiday waits for the Friday that never comes, then the next week");
    }

    [Fact]
    public void A_month_that_ends_on_a_weekend_closes_at_the_last_weekday_with_a_calendar_and_at_the_first_bar_of_the_next_month_without()
    {
        var series = BarFixtures.GetPeriods("month ends on a weekend");
        var calendar = CalendarFixtures.For(series.Bars, series.Holidays);

        var months = Summary(Run(series.Bars, Timeframe.Mo1, calendar));
        var monthsWithout = Summary(Run(series.Bars, Timeframe.Mo1));
        var weeks = Summary(Run(series.Bars, Timeframe.W1, calendar));
        var weeksWithout = Summary(Run(series.Bars, Timeframe.W1));

        months.ShouldBe([(1874, new DateOnly(2026, 1, 1), 1875, true)], "Friday 30 January 15:29; February is not over");
        monthsWithout.ShouldBe([(1875, new DateOnly(2026, 1, 1), 1875, true)], "Monday 2 February 09:15");
        weeks.ShouldBe([(1874, new DateOnly(2026, 1, 26), 1875, true), (3749, new DateOnly(2026, 2, 2), 1875, true)]);
        weeksWithout.ShouldBe([(1875, new DateOnly(2026, 1, 26), 1875, true)]);
    }

    [Fact]
    public void A_week_that_holds_the_end_of_a_month_closes_that_month_in_the_middle_of_the_week_with_a_calendar()
    {
        var series = BarFixtures.GetPeriods("month boundary inside a week");
        var calendar = CalendarFixtures.For(series.Bars, series.Holidays);

        var months = Summary(Run(series.Bars, Timeframe.Mo1, calendar));
        var monthsWithout = Summary(Run(series.Bars, Timeframe.Mo1));
        var weeks = Summary(Run(series.Bars, Timeframe.W1, calendar));

        months.ShouldBe([(749, new DateOnly(2026, 3, 1), 750, true)], "Tuesday 31 March 15:29");
        monthsWithout.ShouldBe([(750, new DateOnly(2026, 3, 1), 750, true)], "Wednesday 1 April 09:15");
        weeks.ShouldBe([(1874, new DateOnly(2026, 3, 30), 1875, true)], "the week of the 30th holds both months; the next week, whose Friday is not in the data, is still open");
    }

    [Fact]
    public void A_saturday_session_known_to_the_calendar_is_the_last_session_of_its_week()
    {
        var series = BarFixtures.GetPeriods("saturday session");
        series.Bars[1874].EpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Fri, Minutes.At(15, 29)));
        series.Bars[1904].EpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Sat, Minutes.Open + 29));
        var calendar = CalendarFixtures.For(series.Bars, series.Holidays);

        var withCalendar = Summary(Run(series.Bars, Timeframe.W1, calendar));
        var without = Summary(Run(series.Bars, Timeframe.W1));

        withCalendar.ShouldBe([(1904, BarFixtures.Mon, 1905, false)], "the 09:44 bar of Saturday, not the Friday close; the Saturday session of 30 bars is incomplete as stored");
        without.ShouldBe([(1905, BarFixtures.Mon, 1905, false)], "Monday 09:15");
    }

    [Fact]
    public void A_saturday_session_the_calendar_did_not_know_is_ignored_and_counted_after_the_friday_close()
    {
        var series = BarFixtures.GetPeriods("saturday session");
        var next = BarFixtures.Walk(new DateOnly(2026, 3, 16), Minutes.Open, 1);
        var calendar = new TradingCalendar();
        var builder = new TimeframeBuilder(Timeframe.W1, calendar);

        var run = BuilderRun.Run([.. series.Bars, .. next], Timeframe.W1, builder);

        Summary(run).ShouldBe([(1874, BarFixtures.Mon, 1875, true), (3030, NextMon, 1125, true)], "the first week is closed by the Friday close and the 30 Saturday bars are not in it");
        builder.Ignored.ShouldBe(30);

        // Without a calendar nothing proves the week is over on Friday: the Saturday session is in the week.
        Summary(Run([.. series.Bars, .. next], Timeframe.W1)).ShouldBe([(1905, BarFixtures.Mon, 1905, false), (3030, NextMon, 1125, true)]);
    }

    [Fact]
    public void When_the_calendar_says_the_friday_is_a_holiday_but_the_data_has_friday_bars_the_week_closes_after_thursday_and_the_friday_bars_are_ignored_and_counted()
    {
        List<Bar> bars = [.. BarFixtures.Walk(BarFixtures.Mon, Minutes.Open, 375), .. BarFixtures.Walk(BarFixtures.Tue, Minutes.Open, 375), .. BarFixtures.Walk(BarFixtures.Wed, Minutes.Open, 375), .. BarFixtures.Walk(BarFixtures.Thu, Minutes.Open, 375),
            .. BarFixtures.Walk(BarFixtures.Fri, Minutes.Open, 375), .. BarFixtures.Walk(NextMon, Minutes.Open, 1)];
        var calendar = CalendarFixtures.For(new Dictionary<DateOnly, (int From, int To)>(), [BarFixtures.Fri]);
        var builder = new TimeframeBuilder(Timeframe.W1, calendar);
        var days = new TimeframeBuilder(Timeframe.D1, calendar);

        var weeks = BuilderRun.Run(bars, Timeframe.W1, builder);
        var fridays = BuilderRun.Run(bars, Timeframe.D1, days).Where(x => SessionClock.DateOf(x.Closed.StartEpochSeconds) == BarFixtures.Fri).ToList();

        Summary(weeks).ShouldBe([(1499, BarFixtures.Mon, 1500, true)], "Thursday 15:29 is the last session of the week the calendar knows: four sessions, not five");
        weeks.Single().Closed.Bar.Close.ShouldBe(bars[1499].Close, "the close of Thursday, not of Friday");
        builder.Ignored.ShouldBe(375, "all the Friday bars, and nothing else: the Monday bar starts the next week");
        fridays.Count.ShouldBe(1, "the day lane does not use the week's calendar view: Friday is a day of its own");
        Summary(Run(bars, Timeframe.W1)).ShouldBe([(1875, BarFixtures.Mon, 1875, true)], "without a calendar Friday is in the week and the week waits for Monday");
    }

    [Fact]
    public void When_the_calendar_says_the_last_weekday_of_a_month_is_a_holiday_but_the_data_has_its_bars_the_month_closes_after_the_day_before_and_those_bars_are_ignored_and_counted()
    {
        // January 2026 ends on a Saturday: Friday the 30th is the last weekday. The calendar calls it a holiday, the data has it.
        var thursday = new DateOnly(2026, 1, 29);
        var friday = new DateOnly(2026, 1, 30);
        List<Bar> bars = [.. BarFixtures.Walk(thursday, Minutes.Open, 375), .. BarFixtures.Walk(friday, Minutes.Open, 375), .. BarFixtures.Walk(new DateOnly(2026, 2, 2), Minutes.Open, 1)];
        var builder = new TimeframeBuilder(Timeframe.Mo1, CalendarFixtures.For(new Dictionary<DateOnly, (int From, int To)>(), [friday]));

        var months = BuilderRun.Run(bars, Timeframe.Mo1, builder);

        Summary(months).ShouldBe([(374, new DateOnly(2026, 1, 1), 375, true)], "Thursday 29 January 15:29 is the last session the calendar knows");
        builder.Ignored.ShouldBe(375, "the Friday bars");
        Summary(Run(bars, Timeframe.Mo1)).ShouldBe([(750, new DateOnly(2026, 1, 1), 750, true)], "without a calendar the month is closed by the first bar of February and holds both days");
    }

    [Fact]
    public void A_saturday_session_the_calendar_did_not_know_at_the_end_of_a_month_is_ignored_and_counted_after_the_last_weekday_and_a_known_one_is_the_last_session()
    {
        // 31 January 2026 is a Saturday. With 30 bars of session on it, a calendar that does not know it closes the month at the Friday close; one that does closes it at the Saturday's last minute.
        var friday = new DateOnly(2026, 1, 30);
        var saturday = new DateOnly(2026, 1, 31);
        List<Bar> bars = [.. BarFixtures.Walk(new DateOnly(2026, 1, 29), Minutes.Open, 375), .. BarFixtures.Walk(friday, Minutes.Open, 375), .. BarFixtures.Walk(saturday, Minutes.Open, 30), .. BarFixtures.Walk(new DateOnly(2026, 2, 2), Minutes.Open, 1)];
        var unaware = new TimeframeBuilder(Timeframe.Mo1, new TradingCalendar());
        var aware = new TimeframeBuilder(Timeframe.Mo1, CalendarFixtures.For(bars, []));

        var withoutSaturday = BuilderRun.Run(bars, Timeframe.Mo1, unaware);
        var withSaturday = BuilderRun.Run(bars, Timeframe.Mo1, aware);

        Summary(withoutSaturday).ShouldBe([(749, new DateOnly(2026, 1, 1), 750, true)], "Friday 30 January 15:29");
        unaware.Ignored.ShouldBe(30, "the Saturday bars are not part of a month that was closed on Friday");
        Summary(withSaturday).ShouldBe([(779, new DateOnly(2026, 1, 1), 780, false)], "the 09:44 bar of Saturday; the session of 30 bars is incomplete as stored");
        aware.Ignored.ShouldBe(0);
        Summary(Run(bars, Timeframe.Mo1)).ShouldBe([(780, new DateOnly(2026, 1, 1), 780, false)], "without a calendar the Saturday is in the month and the first bar of February closes it");
    }

    [Fact]
    public void A_bar_of_a_later_date_of_the_week_ends_a_last_session_that_lost_its_final_minute_and_is_itself_ignored()
    {
        var friday = BarFixtures.GetPeriods("missing friday close").Bars.Take(1874).ToList();
        var saturday = BarFixtures.Walk(BarFixtures.Sat, Minutes.Open, 30);
        var builder = new TimeframeBuilder(Timeframe.W1, new TradingCalendar());
        List<Bar> bars = [.. friday, .. saturday, .. BarFixtures.Walk(NextMon, Minutes.Open, 2)];
        var closedAt = new List<(int Step, long IgnoredAfter, int Bars, bool Complete)>();

        for (var i = 0; i < bars.Count; i++)
        {
            if (builder.Push(bars[i], out var first, out _) > 0)
            {
                closedAt.Add((i, builder.Ignored, first.Bars, first.Complete));
            }
        }

        // The Friday 15:29 bar never came: the 1D bar of Friday, and so the week, is closed by the first Saturday bar, which is itself not part of the week.
        closedAt.ShouldBe([(1874, 1, 1874, false)]);
        builder.Ignored.ShouldBe(30, "all 30 Saturday bars");
    }

    [Fact]
    public void A_week_with_one_session_is_emitted_at_its_close_with_a_calendar_that_knows_the_other_days_are_holidays()
    {
        var series = BarFixtures.GetPeriods("single session week");
        var calendar = CalendarFixtures.For(series.Bars, series.Holidays);

        var withCalendar = Summary(Run(series.Bars, Timeframe.W1, calendar));
        var without = Summary(Run(series.Bars, Timeframe.W1));

        withCalendar.ShouldBe([(1874, BarFixtures.Mon, 1875, true), (2249, new DateOnly(2026, 3, 9), 375, true), (4124, new DateOnly(2026, 3, 16), 1875, true)]);
        without.ShouldBe([(1875, BarFixtures.Mon, 1875, true), (2250, new DateOnly(2026, 3, 9), 375, true)]);
    }

    [Fact]
    public void A_week_without_any_bar_gives_no_bar_and_does_not_disturb_the_weeks_around_it()
    {
        var series = BarFixtures.GetPeriods("holiday only week");
        var calendar = CalendarFixtures.For(series.Bars, series.Holidays);

        var withCalendar = Summary(Run(series.Bars, Timeframe.W1, calendar));
        var without = Summary(Run(series.Bars, Timeframe.W1));

        withCalendar.ShouldBe([(1874, BarFixtures.Mon, 1875, true), (3749, new DateOnly(2026, 3, 16), 1875, true)]);
        without.ShouldBe([(1875, BarFixtures.Mon, 1875, true)]);
    }

    [Fact]
    public void At_a_year_end_the_week_and_the_month_close_at_the_same_step_when_the_first_of_january_is_a_holiday()
    {
        var series = BarFixtures.GetPeriods("year end");
        var calendar = CalendarFixtures.For(series.Bars, series.Holidays);
        var thursday = new DateOnly(2026, 12, 31);
        series.Bars[1499].EpochSeconds.ShouldBe(Minutes.Epoch(thursday, Minutes.At(15, 29)));

        var week = Summary(Run(series.Bars, Timeframe.W1, calendar));
        var month = Summary(Run(series.Bars, Timeframe.Mo1, calendar));
        var weekWithout = Summary(Run(series.Bars, Timeframe.W1));
        var monthWithout = Summary(Run(series.Bars, Timeframe.Mo1));

        week.ShouldBe([(1499, new DateOnly(2026, 12, 28), 1500, true)], "the week of 28 December holds two years and ends on the Thursday");
        month.ShouldBe([(1499, new DateOnly(2026, 12, 1), 1500, true)]);
        weekWithout.ShouldBe([(1500, new DateOnly(2026, 12, 28), 1500, true)]);
        monthWithout.ShouldBe([(1500, new DateOnly(2026, 12, 1), 1500, true)], "4 January is the first bar after December");
    }

    [Fact]
    public void When_the_first_of_january_is_a_session_the_month_closes_on_the_31st_and_the_week_on_the_first()
    {
        List<Bar> bars = [.. BarFixtures.Walk(new DateOnly(2026, 12, 31), Minutes.Open, 375), .. BarFixtures.Walk(new DateOnly(2027, 1, 1), Minutes.Open, 375)];
        var calendar = new TradingCalendar();

        var month = Summary(Run(bars, Timeframe.Mo1, calendar));
        var week = Summary(Run(bars, Timeframe.W1, calendar));

        month.ShouldBe([(374, new DateOnly(2026, 12, 1), 375, true)], "31 December 15:29");
        week.ShouldBe([(749, new DateOnly(2026, 12, 28), 750, true)], "Friday 1 January 15:29");
    }

    [Fact]
    public void February_of_a_leap_year_ends_on_the_29th_and_the_week_that_holds_the_boundary_closes_on_its_friday()
    {
        var leap = BarFixtures.GetPeriods("leap february");
        var calendar = CalendarFixtures.For(leap.Bars, leap.Holidays);
        leap.Bars[2624].EpochSeconds.ShouldBe(Minutes.Epoch(new DateOnly(2028, 2, 29), Minutes.At(15, 29)));

        var february = Summary(Run(leap.Bars, Timeframe.Mo1, calendar));
        var februaryWithout = Summary(Run(leap.Bars, Timeframe.Mo1));
        var weeks = Summary(Run(leap.Bars, Timeframe.W1, calendar));

        february.ShouldBe([(2624, new DateOnly(2028, 2, 1), 2625, true)], "Tuesday 29 February 15:29");
        februaryWithout.ShouldBe([(2625, new DateOnly(2028, 2, 1), 2625, true)], "1 March 09:15");
        weeks.ShouldBe([(1874, new DateOnly(2028, 2, 21), 1875, true), (3749, new DateOnly(2028, 2, 28), 1875, true)], "the week of 28 February holds the 29th and 1 March");
    }

    [Fact]
    public void February_of_a_common_year_ends_on_the_28th_and_its_last_session_is_the_friday_before_that_sunday()
    {
        var common = BarFixtures.GetPeriods("february of a common year");
        var calendar = CalendarFixtures.For(common.Bars, common.Holidays);

        var february = Summary(Run(common.Bars, Timeframe.Mo1, calendar));
        var februaryWithout = Summary(Run(common.Bars, Timeframe.Mo1));

        february.ShouldBe([(1874, new DateOnly(2027, 2, 1), 1875, true)], "Friday 26 February 15:29");
        februaryWithout.ShouldBe([(1875, new DateOnly(2027, 2, 1), 1875, true)], "Monday 1 March 09:15");
    }

    [Fact]
    public void A_stream_that_starts_in_the_middle_of_a_week_gives_that_week_only_the_days_it_saw_and_is_incomplete_only_when_one_of_them_is()
    {
        var atTheOpen = Summary(Run(BarFixtures.GetPeriods("starts mid-week at the open").Bars, Timeframe.W1));
        var midDay = Summary(Run(BarFixtures.GetPeriods("starts mid-week mid-day").Bars, Timeframe.W1));

        atTheOpen.ShouldBe([(1125, BarFixtures.Mon, 1125, true), (3000, NextMon, 1875, true)], "Wednesday to Friday are three whole sessions; the week is complete");
        midDay.ShouldBe([(1020, BarFixtures.Mon, 1020, false), (2895, NextMon, 1875, true)], "Wednesday from 11:00 is a partial session: the first week is incomplete, the next is not");
    }

    [Fact]
    public void A_muhurat_evening_on_the_sunday_is_the_last_session_of_its_week_and_does_not_make_the_week_incomplete()
    {
        var series = BarFixtures.GetPeriods("muhurat sunday");
        var calendar = CalendarFixtures.For(series.Bars, series.Holidays);
        var first = new DateOnly(2026, 11, 2);

        var withCalendar = Summary(Run(series.Bars, Timeframe.W1, calendar));
        var without = Summary(Run(series.Bars, Timeframe.W1));

        withCalendar.ShouldBe([(1934, first, 1935, true)], "the 19:14 bar of Sunday 8 November: a Muhurat day is complete when no minute between its first and last is missing");
        without.ShouldBe([(1935, first, 1935, true)], "Monday 9 November 09:15");
    }

    [Fact]
    public void A_last_session_whose_final_minute_is_missing_closes_the_week_at_the_first_bar_of_the_next_date_as_the_day_does()
    {
        var series = BarFixtures.GetPeriods("missing friday close");
        var regular = new TradingCalendar();
        var shortened = CalendarFixtures.For(series.Bars, series.Holidays);

        var dayCloses = Run(series.Bars, Timeframe.D1, regular).Where(x => SessionClock.DateOf(x.Closed.StartEpochSeconds) == BarFixtures.Fri).ToList();
        var week = Summary(Run(series.Bars, Timeframe.W1, regular)).First();
        var weekShortened = Summary(Run(series.Bars, Timeframe.W1, shortened)).First();

        dayCloses.Single().Step.ShouldBe(1874, "the first bar of Monday: the 15:29 bar of the regular session never came");
        week.ShouldBe((1874, BarFixtures.Mon, 1874, false));
        weekShortened.ShouldBe((1873, BarFixtures.Mon, 1874, false), "a calendar derived from the bars says Friday ended at 15:28, so the session is over at its last bar");
    }

    [Fact]
    public void One_bar_can_close_a_late_week_and_the_one_session_week_that_begins_with_it()
    {
        // The Friday of the first week has no bars at all, but the calendar says it is a session, so Thursday's close does not end the week. The next Monday is a one-minute special session and the only session of its week.
        List<Bar> bars = [.. BarFixtures.Walk(BarFixtures.Mon, Minutes.Open, 375), .. BarFixtures.Walk(BarFixtures.Tue, Minutes.Open, 375), .. BarFixtures.Walk(BarFixtures.Wed, Minutes.Open, 375), .. BarFixtures.Walk(BarFixtures.Thu, Minutes.Open, 375), .. BarFixtures.Walk(NextMon, Minutes.Open, 1)];
        var calendar = CalendarFixtures.For(new Dictionary<DateOnly, (int From, int To)> { [NextMon] = (Minutes.Open, Minutes.Open + 1) }, [.. Enumerable.Range(1, 4).Select(d => NextMon.AddDays(d))]);
        var builder = new TimeframeBuilder(Timeframe.W1, calendar);

        var counts = bars.Take(1500).Select(b => builder.Push(b, out _, out _)).ToList();
        var count = builder.Push(bars[^1], out var first, out var second);

        counts.ShouldAllBe(c => c == 0, "Thursday's close does not end the week: Friday is the last session and it has no bars");
        count.ShouldBe(2);
        (first.StartEpochSeconds, first.Bars, first.Complete).ShouldBe((Minutes.Epoch(BarFixtures.Mon, 0), 1500, true));
        (second.StartEpochSeconds, second.Bars, second.Complete).ShouldBe((Minutes.Epoch(NextMon, 0), 1, false));
    }

    [Fact]
    public void An_evening_bar_after_the_regular_close_of_its_date_is_ignored_and_counted_in_the_week_as_in_the_day()
    {
        List<Bar> bars =
        [
            .. BarFixtures.Walk(BarFixtures.Mon, Minutes.Open, 375),
            Minutes.Bar(BarFixtures.Mon, Minutes.At(18, 15), 1, 2, 0.5, 1.5, 99),
            .. BarFixtures.Walk(BarFixtures.Tue, Minutes.Open, 375),
            .. BarFixtures.Walk(NextMon, Minutes.Open, 1),
        ];
        var week = new TimeframeBuilder(Timeframe.W1);
        var day = new TimeframeBuilder(Timeframe.D1);

        var weeks = BuilderRun.Run(bars, Timeframe.W1, week);
        BuilderRun.Run(bars, Timeframe.D1, day);

        week.Ignored.ShouldBe(1);
        day.Ignored.ShouldBe(1);
        weeks.Single().Closed.Bars.ShouldBe(750, "the evening minute is in neither the day nor the week");
        weeks.Single().Closed.Bar.Volume.ShouldBe(BarFixtures.Walk(BarFixtures.Mon, Minutes.Open, 375).Concat(BarFixtures.Walk(BarFixtures.Tue, Minutes.Open, 375)).Sum(b => b.Volume));
    }

    [Theory]
    [InlineData(Timeframe.W1)]
    [InlineData(Timeframe.Mo1)]
    public void A_bar_at_or_before_the_previous_one_is_ignored_and_counted_and_changes_nothing(Timeframe timeframe)
    {
        var series = BarFixtures.RandomPeriods(5, 90);
        var builder = new TimeframeBuilder(timeframe);
        var expected = Run(series.Bars, timeframe);
        var withNoise = new List<(int Step, ClosedBar Closed)>();

        for (var i = 0; i < series.Bars.Count; i++)
        {
            if (builder.Push(series.Bars[i], out var first, out _) > 0)
            {
                withNoise.Add((i, first));
            }

            if (i % 100 == 99)
            {
                builder.Push(series.Bars[i], out _, out _).ShouldBe(0, "the same bar again");
                builder.Push(series.Bars[i - 50], out _, out _).ShouldBe(0, "an earlier bar");
            }
        }

        expected.ShouldNotBeEmpty();
        withNoise.ShouldBe(expected);
        builder.Ignored.ShouldBe(2 * (series.Bars.Count / 100));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Each_period_is_emitted_exactly_once_in_time_order_and_starts_at_the_monday_or_the_first(int seed)
    {
        var series = BarFixtures.RandomPeriods(seed, 80);

        foreach (var timeframe in PeriodFrames)
        {
            foreach (var kind in Kinds)
            {
                var starts = Run(series.Bars, timeframe, PeriodCalendars.For(series, kind).Calendar).Select(x => x.Closed.StartEpochSeconds).ToList();

                starts.ShouldBe([.. starts.Distinct().OrderBy(s => s)], $"seed {seed}, {timeframe.ToCode()}, {kind}");
                starts.ShouldAllBe(s => SessionClock.MinuteOfDay(s) == 0);
                starts.ShouldAllBe(s => timeframe == Timeframe.W1 ? SessionClock.DateOf(s).DayOfWeek == DayOfWeek.Monday : SessionClock.DateOf(s).Day == 1);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Feeding_in_two_chunks_gives_the_output_of_feeding_at_once(int seed)
    {
        var series = BarFixtures.RandomPeriods(seed, 60);
        var cut = new Random(seed).Next(1, series.Bars.Count);

        foreach (var timeframe in PeriodFrames)
        {
            foreach (var kind in Kinds)
            {
                var calendar = PeriodCalendars.For(series, kind).Calendar;
                var whole = Run(series.Bars, timeframe, calendar);
                var builder = new TimeframeBuilder(timeframe, calendar);
                var chunked = BuilderRun.Run(series.Bars.Take(cut).ToList(), timeframe, builder);
                chunked.AddRange(BuilderRun.Run(series.Bars.Skip(cut).ToList(), timeframe, builder, firstStep: cut));

                chunked.ShouldBe(whole, $"seed {seed}, {timeframe.ToCode()}, {kind}, cut {cut}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(PrefixSeeds))]
    public void The_output_for_the_first_bars_is_the_same_when_more_bars_follow(int seed)
    {
        var series = BarFixtures.RandomPeriods(seed, 40);
        var random = new Random(seed);
        var cuts = new[] { random.Next(1, series.Bars.Count), random.Next(1, series.Bars.Count), series.Bars.Count - 1 };

        foreach (var timeframe in PeriodFrames)
        {
            foreach (var kind in Kinds)
            {
                var calendar = PeriodCalendars.For(series, kind).Calendar;
                var whole = Run(series.Bars, timeframe, calendar);
                foreach (var cut in cuts)
                {
                    var prefix = Run(series.Bars.Take(cut).ToList(), timeframe, calendar);

                    prefix.ShouldBe(whole.Where(x => x.Step < cut).ToList(), $"seed {seed}, {timeframe.ToCode()}, {kind}, first {cut} bars");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_same_series_gives_the_same_periods_every_time_and_after_a_reset(int seed)
    {
        var series = BarFixtures.RandomPeriods(seed, 50);

        foreach (var timeframe in PeriodFrames)
        {
            foreach (var kind in Kinds)
            {
                var calendar = PeriodCalendars.For(series, kind).Calendar;
                var builder = new TimeframeBuilder(timeframe, calendar);
                var first = BuilderRun.Run(series.Bars, timeframe, builder);
                var ignored = builder.Ignored;
                builder.Reset();
                builder.Ignored.ShouldBe(0);
                var again = BuilderRun.Run(series.Bars, timeframe, builder);

                again.ShouldBe(first, $"seed {seed}, {timeframe.ToCode()}, {kind}");
                again.ShouldBe(Run(series.Bars, timeframe, calendar));
                builder.Ignored.ShouldBe(ignored);
            }
        }
    }

    [Fact]
    public void Reset_forgets_the_open_period_the_emitted_one_and_the_order_check()
    {
        var series = BarFixtures.GetPeriods("saturday session");
        var calendar = new TradingCalendar();
        var builder = new TimeframeBuilder(Timeframe.W1, calendar);
        BuilderRun.Run(series.Bars, Timeframe.W1, builder);
        builder.Ignored.ShouldBe(30);

        builder.Reset();
        var again = BuilderRun.Run(series.Bars.Take(1875).ToList(), Timeframe.W1, builder);

        builder.Ignored.ShouldBe(0, "Reset clears the counter, and the earlier week no longer blocks the bars of its dates");
        Summary(again).ShouldBe([(1874, BarFixtures.Mon, 1875, true)]);
    }

    [Theory]
    [InlineData(Timeframe.W1)]
    [InlineData(Timeframe.Mo1)]
    public void The_builder_accepts_the_weekly_and_monthly_timeframes_and_reports_them(Timeframe timeframe)
    {
        var builder = new TimeframeBuilder(timeframe);

        builder.Timeframe.ShouldBe(timeframe);
        builder.Ignored.ShouldBe(0);
    }
}
