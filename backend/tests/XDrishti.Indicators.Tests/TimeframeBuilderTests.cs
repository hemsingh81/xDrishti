using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>The timeframe builder against an independent grouping reference, and its closed-only, exactly-once emission.</summary>
public sealed class TimeframeBuilderTests
{
    public static TheoryData<string, Timeframe> Fixtures
    {
        get
        {
            var data = new TheoryData<string, Timeframe>();
            foreach (var name in BarFixtures.Names)
            {
                foreach (var timeframe in TestSpecs.HigherTimeframes)
                {
                    data.Add(name, timeframe);
                }
            }

            return data;
        }
    }

    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 40)];

    private static void ShouldMatchReference(IReadOnlyList<Bar> bars, Timeframe timeframe, string label, IReadOnlyDictionary<DateOnly, (int From, int To)>? sessions = null)
    {
        var expected = ReferenceBuckets.Build(bars, timeframe, sessions).Where(b => b.CloseIndex != int.MaxValue).OrderBy(b => b.CloseIndex).ThenBy(b => b.Start).ToList();
        var actual = BuilderRun.Run(bars, timeframe, sessions is null ? null : new TimeframeBuilder(timeframe, CalendarFixtures.For(sessions)));

        actual.Count.ShouldBe(expected.Count, label);
        for (var i = 0; i < expected.Count; i++)
        {
            var (step, closed) = actual[i];
            var want = expected[i];
            var where = $"{label}, {timeframe.ToCode()} bucket {i} at {DateTimeOffset.FromUnixTimeSeconds(want.Start):u}";
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
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Buckets_equal_the_grouping_reference_on_every_fixture(string fixture, Timeframe timeframe) =>
        ShouldMatchReference(BarFixtures.Get(fixture), timeframe, fixture);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Buckets_equal_the_grouping_reference_on_random_series_with_special_sessions(int seed)
    {
        var bars = SeriesGenerator.Random(seed, specialSessions: true);

        foreach (var timeframe in TestSpecs.HigherTimeframes)
        {
            ShouldMatchReference(bars, timeframe, $"seed {seed}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void With_a_calendar_buckets_equal_the_calendar_aware_grouping_reference_on_random_series_with_special_sessions(int seed)
    {
        var bars = SeriesGenerator.Random(seed, specialSessions: true);
        var sessions = CalendarFixtures.Windows(bars);

        foreach (var timeframe in TestSpecs.HigherTimeframes)
        {
            ShouldMatchReference(bars, timeframe, $"seed {seed}, with calendar", sessions);
        }
    }

    [Fact]
    public void The_random_series_do_contain_special_and_early_sessions_for_the_calendar_tests()
    {
        var windows = Enumerable.Range(1, 40).Select(seed => CalendarFixtures.Windows(SeriesGenerator.Random(seed, specialSessions: true))).ToList();

        windows.Sum(w => w.Count).ShouldBeGreaterThan(100);
        windows.SelectMany(w => w).Count(w => w.Key.DayOfWeek == DayOfWeek.Saturday).ShouldBeGreaterThan(5);
        windows.SelectMany(w => w).Count(w => w.Value.From >= SessionClock.EveningSessionFromMinute).ShouldBeGreaterThan(5);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void With_a_calendar_buckets_equal_the_calendar_aware_reference_on_every_fixture(string fixture, Timeframe timeframe)
    {
        var bars = BarFixtures.Get(fixture);

        ShouldMatchReference(bars, timeframe, fixture + ", with calendar", CalendarFixtures.Windows(bars));
    }

    [Fact]
    public void A_muhurat_evening_with_a_calendar_closes_the_hour_and_the_day_at_its_own_last_minute()
    {
        var evening = BarFixtures.Get("muhurat evening");
        var calendar = CalendarFixtures.For(evening);

        var hour = BuilderRun.Run(evening, Timeframe.H1, new TimeframeBuilder(Timeframe.H1, calendar)).Single();
        var day = BuilderRun.Run(evening, Timeframe.D1, new TimeframeBuilder(Timeframe.D1, calendar)).Single();

        (hour.Step, hour.Closed.Bars, hour.Closed.Complete).ShouldBe((59, 60, true));
        (day.Step, day.Closed.Bars, day.Closed.Complete).ShouldBe((59, 60, true));
        BuilderRun.Run(evening, Timeframe.D1).ShouldBeEmpty("without the calendar the evening day waits for a later date");
    }

    [Fact]
    public void A_shorter_evening_session_with_a_calendar_closes_its_clipped_hour_at_once_and_flags_it_incomplete()
    {
        var evening = BarFixtures.Walk(BarFixtures.Sun, Minutes.At(18, 15), 50);
        var calendar = CalendarFixtures.For(evening);

        var withCalendar = BuilderRun.Run(evening, Timeframe.H1, new TimeframeBuilder(Timeframe.H1, calendar)).Single();

        (withCalendar.Step, withCalendar.Closed.Bars, withCalendar.Closed.Complete).ShouldBe((49, 50, false));
        BuilderRun.Run(evening, Timeframe.H1).ShouldBeEmpty("without the calendar only a later bucket closes it");
    }

    [Fact]
    public void A_normal_day_has_the_stored_bucket_counts_and_every_bucket_is_complete()
    {
        var bars = BarFixtures.Get("normal day");

        BuilderRun.Run(bars, Timeframe.M5).Count.ShouldBe(75);
        BuilderRun.Run(bars, Timeframe.M10).Count.ShouldBe(38);
        BuilderRun.Run(bars, Timeframe.M15).Count.ShouldBe(25);
        BuilderRun.Run(bars, Timeframe.M30).Count.ShouldBe(13);
        BuilderRun.Run(bars, Timeframe.H1).Count.ShouldBe(7);
        BuilderRun.Run(bars, Timeframe.D1).Count.ShouldBe(1);
        foreach (var timeframe in TestSpecs.HigherTimeframes)
        {
            BuilderRun.Run(bars, timeframe).ShouldAllBe(x => x.Closed.Complete);
        }
    }

    [Fact]
    public void The_last_hour_holds_fifteen_bars_the_last_ten_minute_bucket_five_and_the_last_half_hour_fifteen()
    {
        var bars = BarFixtures.Get("normal day");

        var hour = BuilderRun.Run(bars, Timeframe.H1)[^1];
        var tenMinutes = BuilderRun.Run(bars, Timeframe.M10)[^1];
        var halfHour = BuilderRun.Run(bars, Timeframe.M30)[^1];

        hour.Closed.StartEpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Mon, Minutes.At(15, 15)));
        (hour.Closed.Bars, hour.Closed.Complete).ShouldBe((15, true));
        tenMinutes.Closed.StartEpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Mon, Minutes.At(15, 25)));
        (tenMinutes.Closed.Bars, tenMinutes.Closed.Complete).ShouldBe((5, true));
        (halfHour.Closed.Bars, halfHour.Closed.Complete).ShouldBe((15, true));
        hour.Step.ShouldBe(374);   // closed by the 15:29 bar itself
    }

    [Fact]
    public void A_day_with_missing_minutes_flags_exactly_the_buckets_that_lack_one()
    {
        var bars = BarFixtures.Get("missing minutes");

        var fiveMinute = BuilderRun.Run(bars, Timeframe.M5).Select(x => x.Closed).ToList();
        var day = BuilderRun.Run(bars, Timeframe.D1).Single().Closed;

        fiveMinute.Single(b => b.StartEpochSeconds == Minutes.Epoch(BarFixtures.Mon, 555)).Complete.ShouldBeFalse();   // 09:16 missing
        fiveMinute.Single(b => b.StartEpochSeconds == Minutes.Epoch(BarFixtures.Mon, 565)).Complete.ShouldBeTrue();
        day.Complete.ShouldBeFalse();
        day.Bars.ShouldBe(BarFixtures.Get("missing minutes").Count);
    }

    [Fact]
    public void A_muhurat_evening_is_cut_from_nine_fifteen_and_its_day_is_complete_when_no_minute_between_first_and_last_is_missing()
    {
        var evening = BarFixtures.Get("muhurat evening");
        var next = BarFixtures.Walk(BarFixtures.Mon.AddDays(7), Minutes.Open, 1);

        var day = BuilderRun.Run([.. evening, .. next], Timeframe.D1).Single();
        var hour = BuilderRun.Run(evening, Timeframe.H1).Single();

        day.Closed.StartEpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Sun, 0));
        (day.Closed.Bars, day.Closed.Complete).ShouldBe((60, true));
        day.Step.ShouldBe(60, "an evening session has no regular close: the next date's first bar closes the day");
        hour.Closed.StartEpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Sun, Minutes.At(18, 15)));
        (hour.Closed.Bars, hour.Closed.Complete).ShouldBe((60, true));
        hour.Step.ShouldBe(59, "the 19:14 bar ends the 18:15 hour");
    }

    [Fact]
    public void A_saturday_session_of_thirty_bars_is_a_day_of_its_own_and_incomplete_as_stored()
    {
        var bars = BarFixtures.Get("saturday session");

        var all = BuilderRun.Run([.. bars, .. BarFixtures.Walk(new DateOnly(2026, 3, 9), Minutes.Open, 2)], Timeframe.D1);

        all.Count.ShouldBe(1);
        all[0].Closed.StartEpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Sat, 0));
        (all[0].Closed.Bars, all[0].Closed.Complete).ShouldBe((30, false));
        all[0].Step.ShouldBe(30, "closed by the first bar of the following session");
    }

    [Fact]
    public void A_bucket_is_never_emitted_while_it_is_forming_and_exactly_once_when_its_last_minute_arrives()
    {
        var builder = new TimeframeBuilder(Timeframe.M5);
        var minutes = Enumerable.Range(Minutes.Open, 5).ToList();

        var counts = minutes.Select(m => builder.Push(Minutes.Bar(BarFixtures.Mon, m, 10, 11, 9, 10), out _, out _)).ToList();

        counts.ShouldBe([0, 0, 0, 0, 1]);
    }

    [Fact]
    public void An_incomplete_bucket_is_closed_by_the_first_bar_of_a_later_bucket_and_flagged()
    {
        var builder = new TimeframeBuilder(Timeframe.M5);
        for (var m = Minutes.Open; m < Minutes.Open + 4; m++)
        {
            builder.Push(Minutes.Bar(BarFixtures.Mon, m, 10, 11, 9, 10), out _, out _).ShouldBe(0);
        }

        // 09:19 never comes; 09:20 starts the next bucket
        var closed = builder.Push(Minutes.Bar(BarFixtures.Mon, Minutes.Open + 5, 10, 11, 9, 10), out var first, out _);

        closed.ShouldBe(1);
        first.StartEpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Mon, Minutes.Open));
        (first.Bars, first.Complete).ShouldBe((4, false));
    }

    [Fact]
    public void One_bar_can_close_two_buckets_when_it_is_alone_in_a_bucket_that_ends_at_once()
    {
        var builder = new TimeframeBuilder(Timeframe.M5);
        builder.Push(Minutes.Bar(BarFixtures.Mon, Minutes.Open, 10, 12, 9, 11, 5), out _, out _);

        // 09:24 is the last minute of the 09:20 bucket and its only bar; it also proves the 09:15 bucket (one bar) is over
        var closed = builder.Push(Minutes.Bar(BarFixtures.Mon, Minutes.Open + 9, 20, 22, 19, 21, 7), out var first, out var second);

        closed.ShouldBe(2);
        (first.StartEpochSeconds, first.Bars, first.Complete).ShouldBe((Minutes.Epoch(BarFixtures.Mon, Minutes.Open), 1, false));
        (second.StartEpochSeconds, second.Bars, second.Complete).ShouldBe((Minutes.Epoch(BarFixtures.Mon, Minutes.Open + 5), 1, false));
    }

    [Fact]
    public void A_bar_for_a_bucket_that_was_already_emitted_or_out_of_order_is_ignored_and_counted()
    {
        var builder = new TimeframeBuilder(Timeframe.D1);
        foreach (var bar in BarFixtures.Walk(BarFixtures.Mon, Minutes.Open, 375))
        {
            builder.Push(bar, out _, out _);
        }

        // the day closed at 15:29; an evening bar of the same date would belong to it, an earlier bar is out of order
        builder.Push(Minutes.Bar(BarFixtures.Mon, Minutes.At(18, 15), 1, 1, 1, 1), out _, out _).ShouldBe(0);
        builder.Push(Minutes.Bar(BarFixtures.Mon, Minutes.Open, 1, 1, 1, 1), out _, out _).ShouldBe(0);

        builder.Ignored.ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Feeding_in_two_chunks_gives_the_output_of_feeding_at_once(int seed)
    {
        var bars = SeriesGenerator.Random(seed, 500, 2_000, specialSessions: true);
        var cut = new Random(seed).Next(1, bars.Count);

        foreach (var timeframe in TestSpecs.HigherTimeframes)
        {
            var whole = BuilderRun.Run(bars, timeframe);
            var builder = new TimeframeBuilder(timeframe);
            var chunked = BuilderRun.Run(bars.Take(cut).ToList(), timeframe, builder);
            chunked.AddRange(BuilderRun.Run(bars.Skip(cut).ToList(), timeframe, builder, firstStep: cut));

            chunked.ShouldBe(whole, $"seed {seed}, {timeframe.ToCode()}, cut {cut}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Each_bucket_is_emitted_exactly_once_in_time_order(int seed)
    {
        var bars = SeriesGenerator.Random(seed, specialSessions: true);

        foreach (var timeframe in TestSpecs.HigherTimeframes)
        {
            var starts = BuilderRun.Run(bars, timeframe).Select(x => x.Closed.StartEpochSeconds).ToList();

            starts.ShouldBe([.. starts.Distinct().OrderBy(s => s)], $"seed {seed}, {timeframe.ToCode()}");
        }
    }

    [Fact]
    public void Reset_forgets_the_open_bucket_and_the_order_check()
    {
        var builder = new TimeframeBuilder(Timeframe.M15);
        BuilderRun.Run(BarFixtures.Get("normal day"), Timeframe.M15, builder);
        builder.Reset();

        var again = BuilderRun.Run(BarFixtures.Get("normal day"), Timeframe.M15, builder);

        again.ShouldBe(BuilderRun.Run(BarFixtures.Get("normal day"), Timeframe.M15));
        builder.Ignored.ShouldBe(0);
    }

    // W1 and Mo1 were refused here until P3-01c: they are built now, see WeeklyMonthlyBuilderTests.
    [Theory]
    [InlineData(Timeframe.M1)]
    [InlineData((Timeframe)99)]
    public void Only_the_intraday_timeframes_the_day_the_week_and_the_month_can_be_built(Timeframe timeframe) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new TimeframeBuilder(timeframe));

    [Fact]
    public void A_special_session_in_the_calendar_closes_its_last_bucket_and_its_day_at_its_own_last_minute()
    {
        var saturday = BarFixtures.Sat;
        var calendar = new TradingCalendar([TradingDay.Special(saturday, Minutes.Open, Minutes.Open + 20, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);
        var bars = BarFixtures.Walk(saturday, Minutes.Open, 20);

        var fifteen = BuilderRun.Run(bars, Timeframe.M15, new TimeframeBuilder(Timeframe.M15, calendar));
        var day = BuilderRun.Run(bars, Timeframe.D1, new TimeframeBuilder(Timeframe.D1, calendar));

        fifteen.Select(x => (x.Step, x.Closed.Bars, x.Closed.Complete)).ShouldBe([(14, 15, true), (19, 5, false)]);
        day.Select(x => (x.Step, x.Closed.Bars)).ShouldBe([(19, 20)]);
        BuilderRun.Run(bars, Timeframe.D1).ShouldBeEmpty("without the calendar nothing proves the day is over");
    }

    [Fact]
    public void A_regular_session_in_the_calendar_behaves_like_no_calendar()
    {
        var calendar = new TradingCalendar();
        var bars = BarFixtures.Get("three consecutive days");

        foreach (var timeframe in TestSpecs.HigherTimeframes)
        {
            BuilderRun.Run(bars, timeframe, new TimeframeBuilder(timeframe, calendar)).ShouldBe(BuilderRun.Run(bars, timeframe));
        }
    }
}
