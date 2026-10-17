using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Frame;

/// <summary>
/// ISS-13, adversarial: the day lane never restarts (a daily bar is a whole session) and is never pending; the date-driven features keep their per-date history; gaps in the calendar (weekend, holidays), a Saturday
/// session, a Muhurat evening, a morning block and an evening block on one date, and a half day are all just sessions with the first bar present; and the catalog answers "does this restart" for the twelve names on six
/// timeframes only. Hand-calculated expectations.
/// </summary>
public sealed class DailyAndCalendarAdversarialTests
{
    private static readonly double Nan = double.NaN;

    private static readonly DateOnly[] Days = [BarFixtures.Mon, BarFixtures.Tue, BarFixtures.Wed, BarFixtures.Thu];

    // open, high, low, close, volume of the four daily bars (each told by a 09:15 bar and a 15:29 bar)
    private static readonly (double Open, double High, double Low, double Close, long Volume)[] Daily =
    [
        (100, 110, 95, 105, 1_000),
        (108, 120, 106, 118, 2_000),
        (115, 119, 110, 112, 1_500),
        (111, 125, 109, 124, 3_000),
    ];

    private static double At(double?[] values, int index) => index < 0 || values[index] is null ? Nan : values[index]!.Value;

    [Fact]
    public void The_day_lane_is_never_pending_and_never_restarts_so_every_lag_is_the_previous_days_value()
    {
        // Specs on 1D: values after d closed daily bars, then lag k is the value after d - k bars. gap_pct[1] on 1D is yesterday's gap, not unknown.
        string[] names = ["today_open", "today_high", "today_low", "prev_day_open", "prev_day_high", "prev_day_low", "prev_day_close", "prev_day_volume", "gap_pct", "close"];
        var perDay = new Dictionary<string, double?[]>
        {
            ["today_open"] = [100, 108, 115, 111],
            ["today_high"] = [110, 120, 119, 125],
            ["today_low"] = [95, 106, 110, 109],
            ["prev_day_open"] = [null, 100, 108, 115],
            ["prev_day_high"] = [null, 110, 120, 119],
            ["prev_day_low"] = [null, 95, 106, 110],
            ["prev_day_close"] = [null, 105, 118, 112],
            ["prev_day_volume"] = [null, 1_000, 2_000, 1_500],
            ["gap_pct"] = [null, (108.0 - 105) / 105 * 100, (115.0 - 118) / 118 * 100, (111.0 - 112) / 112 * 100],
            ["close"] = [105, 118, 112, 124],
        };
        var frame = AdversarialFrames.Create([.. names.Select(n => AdversarialFrames.Spec(n, Timeframe.D1))], 4);
        var closed = 0;

        for (var d = 0; d < Days.Length; d++)
        {
            foreach (var bar in BarFixtures.Brief(Days[d], Daily[d].Open, Daily[d].High, Daily[d].Low, Daily[d].Close, Daily[d].Volume))
            {
                frame.Step(bar);

                closed = Math.Max(closed, frame.BarClosed(Timeframe.D1) ? d + 1 : closed);
                for (var h = 0; h < names.Length; h++)
                {
                    for (var k = 0; k < 4; k++)
                    {
                        frame.At(h, k).ShouldBeNear(At(perDay[names[h]], closed - 1 - k), $"{names[h]} (1D) lag {k} at {AdversarialFrames.StepName(bar)} with {closed} daily bar(s) closed");
                    }
                }
            }
        }

        closed.ShouldBe(4);
    }

    [Fact]
    public void A_daily_bar_that_closes_late_is_taken_by_the_next_date_without_blanking_the_day_lane()
    {
        // Monday has bars at 09:15 and 15:28 (no 15:29): its daily bar closes at Tuesday's first minute. Tuesday has 09:15 and 15:29. On the day lane there is nothing to restart: after Tuesday's first minute the lane shows
        // Monday's bar (today_open 100, close 105, no previous day) and not NaN; after Tuesday's close lag 0 is Tuesday's and lag 1 Monday's.
        var specs = new List<FeatureSpec>
        {
            AdversarialFrames.Spec("today_open", Timeframe.D1), AdversarialFrames.Spec("prev_day_close", Timeframe.D1), AdversarialFrames.Spec("gap_pct", Timeframe.D1), AdversarialFrames.Spec("close", Timeframe.D1),
        };
        var frame = AdversarialFrames.Create(specs, 4);
        frame.Step(Minutes.Bar(BarFixtures.Mon, Minutes.Open, 100, 110, 95, 100, 1_000));
        frame.Step(Minutes.Bar(BarFixtures.Mon, Minutes.At(15, 28), 105, 105, 105, 105, 0));

        frame.BarClosed(Timeframe.D1).ShouldBeFalse();

        frame.Step(Minutes.Bar(BarFixtures.Tue, Minutes.Open, 108, 120, 106, 108, 2_000));

        frame.ClosedBarsAt(Timeframe.D1).ShouldBe(1, "the first minute of the next date closes Monday's day");
        frame.ShouldHaveLags(0, [100, Nan, Nan, Nan], "today_open (1D) after the late close");
        frame.ShouldHaveLags(1, [Nan, Nan, Nan, Nan], "prev_day_close (1D): Monday is the first daily bar");
        frame.ShouldHaveLags(3, [105, Nan, Nan, Nan], "close (1D)");

        frame.Step(Minutes.Bar(BarFixtures.Tue, Minutes.At(15, 29), 118, 118, 118, 118, 0));

        frame.ShouldHaveLags(0, [108, 100, Nan, Nan], "today_open (1D)");
        frame.ShouldHaveLags(1, [105, Nan, Nan, Nan], "prev_day_close (1D)");
        frame.ShouldHaveLags(2, [(108.0 - 105) / 105 * 100, Nan, Nan, Nan], "gap_pct (1D)");
    }

    // ---- catalog ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static readonly Timeframe[] NineTimeframes = [Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1, Timeframe.W1, Timeframe.Mo1];

    [Fact]
    public void Only_the_twelve_names_restart_and_only_on_the_six_intraday_timeframes()
    {
        var restarts = new List<string>();
        foreach (var info in IndicatorCatalog.All)
        {
            foreach (var timeframe in NineTimeframes)
            {
                var spec = new FeatureSpec(info.Name, timeframe);
                var expected = AdversarialFrames.Twelve.Contains(info.Name) && AdversarialFrames.Intraday.Contains(timeframe);
                if (info.RestartsEachSession(spec) != expected)
                {
                    restarts.Add($"{info.Name} {timeframe.ToCode()}: expected {expected}");
                }
            }
        }

        restarts.ShouldBeEmpty();
        IndicatorCatalog.All.Count.ShouldBe(56);
        AdversarialFrames.Twelve.Count(name => IndicatorCatalog.TryGet(name, out var _)).ShouldBe(12);
    }

    [Fact]
    public void The_frame_refuses_the_session_features_where_it_always_did_and_accepts_them_where_they_run()
    {
        // vwap and the opening range: the six intraday timeframes only. The other nine: those and 1D. Nothing on 1W and 1M.
        var wrong = new List<string>();
        foreach (var name in AdversarialFrames.Twelve)
        {
            var intradayOnly = name is "vwap" or "or_high" or "or_low";
            foreach (var timeframe in NineTimeframes)
            {
                var expected = AdversarialFrames.Intraday.Contains(timeframe) || (timeframe == Timeframe.D1 && !intradayOnly);
                var spec = AdversarialFrames.TwelveSpec(name, timeframe);
                var created = FeatureFrame.Create([spec]);
                if (created.IsSuccess != expected)
                {
                    wrong.Add($"{name} {timeframe.ToCode()}: expected success {expected}");
                }
            }
        }

        wrong.ShouldBeEmpty();
    }

    // ---- dates -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Date_driven_features_are_ready_at_the_first_step_of_a_date_and_keep_one_history_slot_per_date()
    {
        // day_of_week is 1 on Monday, 2 on Tuesday, 3 on Wednesday (ISO). It is refreshed once per date: lag 1 is the previous date's weekday from the very first step of the date, never NaN there (it is not a restarting
        // feature), and a skipped bar (a duplicate of Tuesday's first, an old Monday bar after Wednesday's first) refreshes nothing.
        var frame = AdversarialFrames.Create([AdversarialFrames.Spec("day_of_week", Timeframe.M1), AdversarialFrames.Spec("vwap", Timeframe.M1)], 4);

        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open, 100));
        frame.ShouldHaveLags(0, [1, Nan, Nan, Nan], "Monday bar 1");
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 1, 100));
        frame.ShouldHaveLags(0, [1, Nan, Nan, Nan], "Monday bar 2");

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open, 100));
        frame.IsReady(0).ShouldBeTrue("a date-driven feature is not pending at the first step of a date");
        frame.ShouldHaveLags(0, [2, 1, Nan, Nan], "Tuesday bar 1");
        frame.ShouldHaveLags(1, [100, Nan, Nan, Nan], "vwap restarts while day_of_week keeps its slot");

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open, 100));
        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + 1, 100));
        frame.ShouldHaveLags(0, [2, 1, Nan, Nan], "Tuesday bar 2 after a skipped duplicate");

        frame.Step(Minutes.Close(BarFixtures.Wed, Minutes.Open, 100));
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 5, 100));
        frame.ShouldHaveLags(0, [3, 2, 1, Nan], "Wednesday bar 1 after a skipped Monday bar");
        frame.IgnoredBars.ShouldBe(2);
    }

    // ---- calendars -------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_first_bar_after_a_weekend_and_after_a_holiday_gap_starts_a_session_whose_previous_day_is_the_last_session_that_had_bars()
    {
        // Friday 6 March closes 100, 102; Monday 9 closes 110, 112; Tuesday 10 and Wednesday 11 are holidays; Thursday 12 closes 105, 99. Volume 10 on every bar, so the vwap is the running mean.
        var frame = AdversarialFrames.Create([AdversarialFrames.Spec("today_open", Timeframe.M1), AdversarialFrames.Spec("prev_day_close", Timeframe.M1), AdversarialFrames.Spec("vwap", Timeframe.M1), AdversarialFrames.Spec("gap_pct", Timeframe.M1)], 4);
        var mon = new DateOnly(2026, 3, 9);
        var thu = new DateOnly(2026, 3, 12);
        frame.Step(Minutes.Close(BarFixtures.Fri, Minutes.Open, 100));
        frame.Step(Minutes.Close(BarFixtures.Fri, Minutes.Open + 1, 102));

        frame.Step(Minutes.Close(mon, Minutes.Open, 110));

        frame.ShouldHaveLags(0, [110, Nan, Nan, Nan], "Monday bar 1 today_open");
        frame.ShouldHaveLags(1, [102, Nan, Nan, Nan], "Monday bar 1 prev_day_close is Friday's");
        frame.ShouldHaveLags(2, [110, Nan, Nan, Nan], "Monday bar 1 vwap");
        frame.ShouldHaveLags(3, [(110.0 - 102) / 102 * 100, Nan, Nan, Nan], "Monday bar 1 gap_pct");

        frame.Step(Minutes.Close(mon, Minutes.Open + 1, 112));

        frame.ShouldHaveLags(1, [102, 102, Nan, Nan], "Monday bar 2 prev_day_close");
        frame.ShouldHaveLags(2, [111, 110, Nan, Nan], "Monday bar 2 vwap");

        frame.Step(Minutes.Close(thu, Minutes.Open, 105));

        frame.ShouldHaveLags(1, [112, Nan, Nan, Nan], "Thursday bar 1 prev_day_close is Monday's last, two holidays between");
        frame.ShouldHaveLags(3, [(105.0 - 112) / 112 * 100, Nan, Nan, Nan], "Thursday bar 1 gap_pct");

        frame.Step(Minutes.Close(thu, Minutes.Open + 1, 99));

        frame.ShouldHaveLags(2, [102, 105, Nan, Nan], "Thursday bar 2 vwap");
        frame.ShouldHaveLags(0, [105, 105, Nan, Nan], "Thursday bar 2 today_open");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_saturday_session_and_a_muhurat_evening_are_sessions_of_their_own_with_or_without_a_calendar(bool useCalendar)
    {
        // Friday 100 101; Saturday 7 March 09:15 120 121; Sunday 8 March 18:15 130 131; Monday 9 March 09:15 140. Volume 10 per bar.
        var sat = new DateOnly(2026, 3, 7);
        var sun = new DateOnly(2026, 3, 8);
        var mon = new DateOnly(2026, 3, 9);
        var bars = new List<Bar>
        {
            Minutes.Close(BarFixtures.Fri, Minutes.Open, 100), Minutes.Close(BarFixtures.Fri, Minutes.Open + 1, 101),
            Minutes.Close(sat, Minutes.Open, 120), Minutes.Close(sat, Minutes.Open + 1, 121),
            Minutes.Close(sun, Minutes.At(18, 15), 130), Minutes.Close(sun, Minutes.At(18, 16), 131),
            Minutes.Close(mon, Minutes.Open, 140),
        };
        var calendar = useCalendar ? CalendarFixtures.For(bars) : null;
        var frame = AdversarialFrames.Create([AdversarialFrames.Spec("today_open", Timeframe.M1), AdversarialFrames.Spec("prev_day_close", Timeframe.M1), AdversarialFrames.Spec("vwap", Timeframe.M1)], 4, calendar);

        foreach (var bar in bars.Take(3))
        {
            frame.Step(bar);
        }

        frame.ShouldHaveLags(0, [120, Nan, Nan, Nan], "Saturday bar 1 today_open");
        frame.ShouldHaveLags(1, [101, Nan, Nan, Nan], "Saturday bar 1 prev_day_close is Friday's last");
        frame.ShouldHaveLags(2, [120, Nan, Nan, Nan], "Saturday bar 1 vwap");

        frame.Step(bars[3]);
        frame.ShouldHaveLags(2, [120.5, 120, Nan, Nan], "Saturday bar 2 vwap");

        frame.Step(bars[4]);
        frame.ShouldHaveLags(0, [130, Nan, Nan, Nan], "Sunday evening bar 1 today_open: the evening starts a session at 18:15");
        frame.ShouldHaveLags(1, [121, Nan, Nan, Nan], "Sunday evening prev_day_close is Saturday's last");
        frame.ShouldHaveLags(2, [130, Nan, Nan, Nan], "Sunday evening vwap");

        frame.Step(bars[5]);
        frame.ShouldHaveLags(2, [130.5, 130, Nan, Nan], "Sunday evening bar 2 vwap");

        frame.Step(bars[6]);
        frame.ShouldHaveLags(0, [140, Nan, Nan, Nan], "Monday bar 1 today_open");
        frame.ShouldHaveLags(1, [131, Nan, Nan, Nan], "Monday bar 1 prev_day_close is the Muhurat evening's last");
    }

    [Fact]
    public void A_morning_block_and_an_evening_block_on_one_date_are_one_session_by_the_documented_limit()
    {
        // Tuesday 09:15 close 100, 09:16 close 110, then 18:15 close 130, all on the same India date: the session does not restart at 18:15, the vwap is the running mean of the three (volume 10 each) and the history has three slots.
        var frame = AdversarialFrames.Create([AdversarialFrames.Spec("vwap", Timeframe.M1), AdversarialFrames.Spec("today_open", Timeframe.M1), AdversarialFrames.Spec("today_high", Timeframe.M1)], 4);
        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open, 100));
        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + 1, 110));

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.At(18, 15), 130));

        frame.ShouldHaveLags(0, [340.0 / 3, 105, 100, Nan], "vwap with the evening block in the same session");
        frame.ShouldHaveLags(1, [100, 100, 100, Nan], "today_open");
        frame.ShouldHaveLags(2, [130, 110, 100, Nan], "today_high");
    }

    [Fact]
    public void A_half_day_with_a_calendar_closes_its_last_hour_at_its_own_last_minute_and_the_next_date_restarts_normally()
    {
        // Tuesday 09:15-10:59 only: the hour 09:15 (price 100) closes at 10:14, the hour 10:15 (price 110) only has 45 minutes. A calendar that knows the session closes it at 10:59 itself, so Wednesday's first minute closes
        // nothing and the 1h lane is pending on Wednesday until 10:14 (price 120, 60 minutes). ema(2) on 1h: 105 after Tuesday, then 105 + 2/3 (120 - 105) = 115.
        var tuesday = new List<Bar>();
        tuesday.AddRange(Enumerable.Range(0, 60).Select(i => Minutes.Close(BarFixtures.Tue, Minutes.Open + i, 100)));
        tuesday.AddRange(Enumerable.Range(60, 45).Select(i => Minutes.Close(BarFixtures.Tue, Minutes.Open + i, 110)));
        var wednesday = Enumerable.Range(0, 60).Select(i => Minutes.Close(BarFixtures.Wed, Minutes.Open + i, 120)).ToList();
        var calendar = new TradingCalendar([TradingDay.Special(BarFixtures.Tue, Minutes.Open, Minutes.Open + 105, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);
        List<FeatureSpec> specs =
        [
            AdversarialFrames.Spec("today_open", Timeframe.H1), AdversarialFrames.Spec("prev_day_close", Timeframe.H1), AdversarialFrames.Spec("vwap", Timeframe.H1), AdversarialFrames.Spec("ema", Timeframe.H1, 2),
        ];

        var withCalendar = AdversarialFrames.Create(specs, 4, calendar);
        foreach (var bar in tuesday)
        {
            withCalendar.Step(bar);
        }

        withCalendar.ClosedBarsAt(Timeframe.H1).ShouldBe(1, "the calendar closes the half hour at 10:59");
        withCalendar.ShouldHaveLags(0, [100, 100, Nan, Nan], "today_open (1h) after Tuesday");
        withCalendar.ShouldHaveLags(3, [105, Nan, Nan, Nan], "ema(2) after two Tuesday hours");

        withCalendar.Step(wednesday[0]);

        withCalendar.BarClosed(Timeframe.H1).ShouldBeFalse("nothing is late: the calendar closed the hour");
        withCalendar.ShouldHaveLags(0, [Nan, Nan, Nan, Nan], "today_open (1h) is pending on Wednesday");
        withCalendar.ShouldHaveLags(1, [Nan, Nan, Nan, Nan], "prev_day_close (1h) is pending on Wednesday");
        withCalendar.ShouldHaveLags(3, [105, Nan, Nan, Nan], "ema(2) is untouched");

        foreach (var bar in wednesday.Skip(1))
        {
            withCalendar.Step(bar);
        }

        withCalendar.ShouldHaveLags(0, [120, Nan, Nan, Nan], "today_open (1h) after Wednesday's first hour");
        withCalendar.ShouldHaveLags(1, [110, Nan, Nan, Nan], "prev_day_close (1h) is Tuesday's half hour");
        withCalendar.ShouldHaveLags(2, [120, Nan, Nan, Nan], "vwap (1h)");
        withCalendar.ShouldHaveLags(3, [115, 105, Nan, Nan], "ema(2)");

        // without the calendar the half hour closes late, at Wednesday's first minute, and is taken with Tuesday's date
        var withoutCalendar = AdversarialFrames.Create(specs, 4);
        foreach (var bar in tuesday)
        {
            withoutCalendar.Step(bar);
        }

        withoutCalendar.ShouldHaveLags(0, [100, Nan, Nan, Nan], "today_open (1h) after Tuesday, no calendar: one hour closed");
        withoutCalendar.ShouldHaveLags(3, [Nan, Nan, Nan, Nan], "ema(2) has one bar");

        withoutCalendar.Step(wednesday[0]);

        withoutCalendar.ClosedBarsAt(Timeframe.H1).ShouldBe(1, "Wednesday's first minute closes Tuesday's half hour");
        withoutCalendar.ShouldHaveLags(0, [Nan, Nan, Nan, Nan], "today_open (1h) is pending");
        withoutCalendar.ShouldHaveLags(3, [105, Nan, Nan, Nan], "ema(2) took the late bar");

        foreach (var bar in wednesday.Skip(1))
        {
            withoutCalendar.Step(bar);
        }

        withoutCalendar.ShouldHaveLags(0, [120, Nan, Nan, Nan], "today_open (1h) after Wednesday's first hour");
        withoutCalendar.ShouldHaveLags(1, [110, Nan, Nan, Nan], "prev_day_close (1h)");
        withoutCalendar.ShouldHaveLags(3, [115, 105, Nan, Nan], "ema(2)");
    }
}
