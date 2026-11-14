namespace XDrishti.SimReader.Tests;

/// <summary>
/// Hand-computed series of BASE_BREAKOUT v1 (spec P3-06; tick 0.05, base_len 20, base_pct 8, vol_x 1.5, rr 2, hold 5 sessions).
/// Frame: 80 sessions from Monday 2026-01-05 (weekdays only), close c_i = 100 + 0.1 i, low = close - 0.5, volume 375,000 (1000 a minute); the signal session is the 80th (c = 107.90, low 107.40),
/// with twice the volume. Base: the last 20 closes 106.00 to 107.90 span 1.8 % (&lt;= 8). Stop = lowest low of the last 20 sessions = 105.50. Limit = tickDown(107.90 x 1.001 = 108.0079) = 108.00.
/// The sessions after it are flat at 108.00: entry at the open of the next session 108.00, risk 2.50, target 113.00.
/// </summary>
public sealed class BaseBreakoutReaderTests
{
    private static readonly BaseBreakoutStrategy Strategy = BaseBreakoutStrategy.V1(0.05m);

    private static IEnumerable<DateOnly> Weekdays(DateOnly from, int count, params DateOnly[] holidays)
    {
        var date = from;
        var produced = 0;
        while (produced < count)
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(date))
            {
                produced++;
                yield return date;
            }

            date = date.AddDays(1);
        }
    }

    private static ReaderSession Session(DateOnly date, decimal close, decimal volumeFactor = 1m, Action<DatedDay>? shape = null)
    {
        var day = new DatedDay(date).Bar("09:15", close, close, close - 0.5m, close, 1000m * volumeFactor);
        for (var m = (9 * 60) + 16; m < (15 * 60) + 30; m++)
        {
            day.Bar(TimeSpan.FromMinutes(m).ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture), close, close, close, close, 1000m * volumeFactor);
        }

        shape?.Invoke(day);
        return day.Build();
    }

    /// <summary>The history of closes (volume 1 each) ending in the signal session, then <paramref name="after"/> flat sessions at 108.00 whose minutes <paramref name="shape"/> may change by session number (1 = the fill session).</summary>
    private static (List<ReaderSession> Sessions, DateOnly Signal) Series(List<decimal> closes, decimal signalVolume = 2m, int after = 6, Action<int, DatedDay>? shape = null, DateOnly[]? holidays = null, decimal afterPrice = 108m)
    {
        var dates = Weekdays(new DateOnly(2026, 1, 5), closes.Count + after, holidays ?? []).ToList();
        var sessions = new List<ReaderSession>();
        for (var i = 0; i < closes.Count; i++)
        {
            sessions.Add(Session(dates[i], closes[i], i == closes.Count - 1 ? signalVolume : 1m));
        }

        for (var k = 1; k <= after; k++)
        {
            var session = k;
            sessions.Add(Session(dates[closes.Count - 1 + k], afterPrice, 1m, d => shape?.Invoke(session, d)));
        }

        return (sessions, dates[closes.Count - 1]);
    }

    private static List<decimal> Rising(int count = 80) => [.. Enumerable.Range(0, count).Select(i => 100m + (0.1m * (i + 80 - count)))];

    private static ReaderReading? Read((List<ReaderSession> Sessions, DateOnly Signal) s, BaseBreakoutStrategy? strategy = null) =>
        BaseBreakoutReader.Read(s.Sessions, s.Signal, strategy ?? Strategy);

    private static long Epoch(ReaderSession s, string hhmm) => s.Bars.Single(b => ((b.EpochSeconds + 19800) % 86400) / 60 == DatedDay.Minute(hhmm)).EpochSeconds;

    [Fact]
    public void B01_Max_hold_leaves_at_the_close_of_the_fifth_session()
    {
        var s = Series(Rising());

        var r = Read(s);

        r.ShouldNotBeNull();
        var fill = s.Sessions[80];
        var due = s.Sessions[84];
        r.TriggerClose.ShouldBe(107.9m);
        r.EntryEpoch.ShouldBe(Epoch(fill, "09:15"));
        r.EntryPrice.ShouldBe(108m);
        r.EntryAtLimit.ShouldBeFalse();
        r.Stop.ShouldBe(105.5m);
        r.Target.ShouldBe(113m);
        r.ExitEpoch.ShouldBe(Epoch(due, "15:29"));
        r.ExitPrice.ShouldBe(108m);
        r.ExitReason.ShouldBe(ReaderExitReason.MaxHold);
        r.SeenR.ShouldBe(0m);
    }

    [Fact]
    public void B02_The_target_is_a_touch_on_a_later_session()
    {
        var s = Series(Rising(), shape: (k, d) => { if (k == 3) { d.Bar("11:00", 108m, 113m, 108m, 108m); } });

        var r = Read(s);

        r.ShouldNotBeNull();
        r.ExitEpoch.ShouldBe(Epoch(s.Sessions[82], "11:00"));
        r.ExitPrice.ShouldBe(113m);
        r.ExitReason.ShouldBe(ReaderExitReason.Target);
        r.SeenR.ShouldBe(2m);
    }

    [Fact]
    public void B03_The_stop_is_a_touch_on_the_second_session()
    {
        var s = Series(Rising(), shape: (k, d) => { if (k == 2) { d.Bar("10:00", 108m, 108m, 105.5m, 108m); } });

        var r = Read(s);

        r.ShouldNotBeNull();
        r.ExitPrice.ShouldBe(105.5m);
        r.ExitReason.ShouldBe(ReaderExitReason.Stop);
        r.SeenR.ShouldBe(-1m);
    }

    [Fact]
    public void B04_An_overnight_gap_through_the_stop_exits_at_the_open()
    {
        var s = Series(Rising(), shape: (k, d) => { if (k == 2) { d.Bar("09:15", 105m, 105m, 105m, 105m); } });

        var r = Read(s);

        r.ShouldNotBeNull();
        r.ExitEpoch.ShouldBe(Epoch(s.Sessions[81], "09:15"));
        r.ExitPrice.ShouldBe(105m);
        r.ExitReason.ShouldBe(ReaderExitReason.Stop);
        r.SeenR.ShouldBe(-1.2m);
    }

    [Fact]
    public void B05_A_gap_above_the_limit_that_does_not_trade_back_is_no_trade()
    {
        Read(Series(Rising(), shape: (k, d) => { if (k == 1) { d.Bar("09:15", 109m, 109m, 109m, 109m); } })).ShouldBeNull();
    }

    [Fact]
    public void B06_A_gap_above_the_limit_that_trades_back_fills_at_the_limit_and_skips_the_target_test()
    {
        var s = Series(Rising(), shape: (k, d) => { if (k == 1) { d.Bar("09:15", 108.5m, 120m, 107.9m, 108m); } });

        var r = Read(s);

        r.ShouldNotBeNull();
        r.EntryPrice.ShouldBe(108m);
        r.EntryAtLimit.ShouldBeTrue();
        r.ExitReason.ShouldBe(ReaderExitReason.MaxHold);
    }

    [Fact]
    public void B07_The_fill_must_be_in_the_first_five_minutes()
    {
        var s = Series(Rising(), shape: (k, d) => { if (k == 1) { d.Hole("09:15", "09:20"); } });

        Read(s).ShouldBeNull();
    }

    [Fact]
    public void B08_A_fill_at_09_20_still_counts()
    {
        var s = Series(Rising(), shape: (k, d) => { if (k == 1) { d.Hole("09:15", "09:19"); } });

        Read(s)!.EntryEpoch.ShouldBe(Epoch(s.Sessions[80], "09:20"));
    }

    [Fact]
    public void B09_An_open_at_or_through_the_stop_is_no_entry()
    {
        Read(Series(Rising(), shape: (k, d) => { if (k == 1) { d.Bar("09:15", 105.5m, 106m, 105.5m, 106m); } })).ShouldBeNull();
    }

    [Fact]
    public void B10_The_volume_needs_vol_x_times_the_mean_and_a_tie_counts()
    {
        Read(Series(Rising(), signalVolume: 1.5m)).ShouldNotBeNull();
        Read(Series(Rising(), signalVolume: 1.4m)).ShouldBeNull();
    }

    [Fact]
    public void B11_The_base_may_span_at_most_base_pct()
    {
        // the closes of the last 20 sessions span 1.8 %
        Read(Series(Rising()), Strategy with { BasePct = 1.9m }).ShouldNotBeNull();
        Read(Series(Rising()), Strategy with { BasePct = 1.7m }).ShouldBeNull();
    }

    [Fact]
    public void B12_The_close_must_reach_the_base_high_and_a_tie_counts()
    {
        var below = Rising();
        below[79] = 107.7m;
        var tie = Rising();
        tie[79] = 107.8m;

        Read(Series(below)).ShouldBeNull();
        Read(Series(tie)).ShouldNotBeNull();
    }

    [Fact]
    public void B13_The_daily_trend_needs_the_close_above_the_50_day_average()
    {
        var closes = Rising();
        for (var i = 0; i < 60; i++)
        {
            closes[i] = 115m; // a high plateau: today's 107.9 is below the average of the last 50
        }

        Read(Series(closes)).ShouldBeNull();
    }

    [Fact]
    public void B14_No_trade_with_less_than_50_sessions_of_history()
    {
        Read(Series(Rising(49))).ShouldBeNull();
        Read(Series(Rising(50))).ShouldNotBeNull();
    }

    [Fact]
    public void B15_A_late_daily_bar_loses_the_signal()
    {
        var s = Series(Rising());
        var late = s.Sessions[79];
        s.Sessions[79] = late with { Bars = [.. late.Bars.Where(b => b.EpochSeconds != Epoch(late, "15:29"))] };

        Read(s).ShouldBeNull();
    }

    [Fact]
    public void B16_No_trade_without_a_next_session_and_a_list_that_ends_early_is_end_of_data()
    {
        Read(Series(Rising(), after: 0)).ShouldBeNull();

        var r = Read(Series(Rising(), after: 3));

        r.ShouldNotBeNull();
        r.ExitReason.ShouldBe(ReaderExitReason.EndOfData);
    }

    /// <summary>Flat closes 100 with a signal close 100.4: weekly mean of the last 10 closed weeks 100.04 only when the signal's own week is closed.</summary>
    private static List<decimal> Plateau()
    {
        var closes = Enumerable.Repeat(100m, 80).ToList();
        closes[79] = 100.4m;
        return closes;
    }

    [Fact]
    public void B17_The_weekly_trend_counts_the_signal_week_only_when_the_signal_is_the_last_session_of_its_week()
    {
        // 2026-01-05 is a Monday; session 80 (index 79) falls on a Friday: 16 weeks of 5 sessions
        var friday = Series(Plateau(), afterPrice: 100.4m);
        friday.Signal.DayOfWeek.ShouldBe(DayOfWeek.Friday);

        // one session earlier the signal is a Thursday: the last closed week is the previous one, whose close 100 is not above its mean 100
        var thursday = Series(Plateau().Skip(1).ToList(), afterPrice: 100.4m);
        thursday.Signal.DayOfWeek.ShouldBe(DayOfWeek.Thursday);

        Read(friday).ShouldNotBeNull();
        Read(thursday).ShouldBeNull();
    }

    [Fact]
    public void B18_A_holiday_makes_the_thursday_the_last_session_of_its_week()
    {
        var thursday = Series(Plateau().Skip(1).ToList(), afterPrice: 100.4m);
        var withHoliday = Series(Plateau().Skip(1).ToList(), holidays: [thursday.Signal.AddDays(1)], afterPrice: 100.4m);

        withHoliday.Signal.ShouldBe(thursday.Signal);
        Read(thursday).ShouldBeNull();
        Read(withHoliday).ShouldNotBeNull();
    }

    [Fact]
    public void B19_The_reader_refuses_a_bad_strategy_and_a_missing_signal_date_is_no_trade()
    {
        var s = Series(Rising());

        Should.Throw<ArgumentException>(() => Read(s, Strategy with { BasePct = 0m }));
        BaseBreakoutReader.Read(s.Sessions, new DateOnly(2030, 1, 1), Strategy).ShouldBeNull();
    }
}
