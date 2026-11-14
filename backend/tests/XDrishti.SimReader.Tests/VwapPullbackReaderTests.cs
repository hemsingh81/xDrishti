namespace XDrishti.SimReader.Tests;

/// <summary>
/// Hand-computed days of VWAP_PULLBACK (spec P3-05 D9, tick 0.05, pull_atr 0.5, rr 2). Prefix: 21 sessions of 15-minute bars c / c+0.5 / c-0.5 / c at c = 100 + 0.5k (an up-trend ending at 110.50), so ATR(14) is exactly 1.00.
/// Fixture day V1, six bars b1 to b6 at 09:15 to 10:30 (first minute of each bar, the rest of the bar repeats its close):
/// vwap 111.0833, 111.4583, 111.8333, 112.2083, 112.3667, 112.5694; the trigger bar b6 (113.00/114.00/113.00/113.75) closes above vwap and open, b5 and b4 closed above vwap, the dip bar b5 has a lower low (112.50) than b4 (112.75)
/// and 112.50 &lt;= 112.3667 + 0.5. Decision 10:45; limit = tickDown(113.75 x 1.001 = 113.86375) = 113.85; entry at the 10:45 open 113.75; stop = lower of the dip and trigger lows 112.50; risk 1.25; target 116.25.
/// </summary>
public sealed class VwapPullbackReaderTests
{
    private static readonly VwapPullbackStrategy Strategy = VwapPullbackStrategy.V1(0.05m);

    private static List<ReaderSession> Prefix(int sessions, bool up = true)
    {
        var result = new List<ReaderSession>();
        for (var k = 1; k <= sessions; k++)
        {
            var c = 100m + (0.5m * k);
            var day = new DatedDay(new DateOnly(2026, 1, 1).AddDays(k));
            for (var bar = 0; bar < 25; bar++)
            {
                var at = TimeSpan.FromMinutes((9 * 60) + 15 + (bar * 15)).ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
                day.Bar(at, c, c + 0.5m, c - 0.5m, c);
                for (var m = 1; m < 15; m++)
                {
                    if ((bar * 15) + m < 375)
                    {
                        day.Flat(TimeSpan.FromMinutes((9 * 60) + 15 + (bar * 15) + m).ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture), c);
                    }
                }
            }

            result.Add(up ? day.Build() : DatedDay.Mirror(day.Build(), 110.5m));
        }

        return result;
    }

    private static DateOnly FixtureDate(int sessions) => new DateOnly(2026, 1, 1).AddDays(sessions + 1);

    /// <summary>V1's six bars, then flat 113.75 to the close (a flat 113.75 repeats the b6 close).</summary>
    private static DatedDay V1(int sessions = 21) => new DatedDay(FixtureDate(sessions))
        .Bar("09:15", 110.5m, 111.5m, 110.5m, 111.25m)
        .Bar("09:30", 111.25m, 112.25m, 111.25m, 112m)
        .Bar("09:45", 112m, 113m, 112m, 112.75m)
        .Bar("10:00", 112.75m, 113.75m, 112.75m, 113.5m)
        .Bar("10:15", 113.5m, 113.5m, 112.5m, 113m)
        .Bar("10:30", 113m, 114m, 113m, 113.75m);

    private static ReaderReading? Buy(DatedDay day, int sessions = 21, VwapPullbackStrategy? strategy = null) =>
        VwapPullbackReader.Read(Prefix(sessions), day.Bars(), strategy ?? Strategy, ReaderSide.Buy);

    [Fact]
    public void V01_Buy_trade_reaches_the_target()
    {
        var day = V1().Bar("11:30", 113.75m, 116.25m, 113.75m, 116m);

        var r = Buy(day);

        r.ShouldNotBeNull();
        r.TriggerBarStartEpoch.ShouldBe(day.Epoch("10:30"));
        r.TriggerClose.ShouldBe(113.75m);
        r.EntryEpoch.ShouldBe(day.Epoch("10:45"));
        r.EntryPrice.ShouldBe(113.75m);
        r.EntryAtLimit.ShouldBeFalse();
        r.Stop.ShouldBe(112.5m);
        r.Target.ShouldBe(116.25m);
        r.ExitEpoch.ShouldBe(day.Epoch("11:30"));
        r.ExitPrice.ShouldBe(116.25m);
        r.ExitReason.ShouldBe(ReaderExitReason.Target);
        r.SeenR.ShouldBe(2m);
    }

    [Fact]
    public void V02_Buy_trade_hits_the_stop()
    {
        var day = V1().Bar("11:05", 113.75m, 113.75m, 112.5m, 112.5m);

        var r = Buy(day);

        r.ShouldNotBeNull();
        r.ExitEpoch.ShouldBe(day.Epoch("11:05"));
        r.ExitPrice.ShouldBe(112.5m);
        r.ExitReason.ShouldBe(ReaderExitReason.Stop);
        r.SeenR.ShouldBe(-1m);
    }

    [Fact]
    public void V03_Buy_trade_is_squared_off_at_the_open_of_15_15()
    {
        var day = V1().Bar("15:15", 113.5m, 113.5m, 113.5m, 113.5m);

        var r = Buy(day);

        r.ShouldNotBeNull();
        r.ExitEpoch.ShouldBe(day.Epoch("15:15"));
        r.ExitPrice.ShouldBe(113.5m);
        r.ExitReason.ShouldBe(ReaderExitReason.SquareOff);
        r.SeenR.ShouldBe(-0.2m);
    }

    [Fact]
    public void V04_A_gap_through_the_stop_exits_at_the_open()
    {
        var day = V1().Bar("12:00", 112m, 112m, 112m, 112m);

        var r = Buy(day);

        r.ShouldNotBeNull();
        r.ExitPrice.ShouldBe(112m);
        r.ExitReason.ShouldBe(ReaderExitReason.Stop);
        r.SeenR.ShouldBe(-1.4m);
    }

    [Fact]
    public void V05_The_entry_is_at_the_limit_when_the_fill_minute_opens_above_it_and_trades_back()
    {
        // fill minute opens 113.95 > limit 113.85, its low 113.80 touches the limit: entry at 113.85; the target is not tested in the fill minute (X5)
        var day = V1().Bar("10:45", 113.95m, 116.5m, 113.8m, 113.9m);

        var r = Buy(day);

        r.ShouldNotBeNull();
        r.EntryPrice.ShouldBe(113.85m);
        r.EntryAtLimit.ShouldBeTrue();
        r.Target.ShouldBe(113.85m + (2m * 1.35m));
        r.ExitReason.ShouldBe(ReaderExitReason.SquareOff);
    }

    [Fact]
    public void V06_No_trade_when_the_fill_minute_never_reaches_the_limit()
    {
        var day = V1().Bar("10:45", 114m, 114.5m, 113.9m, 114m);

        Buy(day).ShouldBeNull();
    }

    [Fact]
    public void V07_Sell_mirror_gives_the_mirrored_trade()
    {
        var mirrored = DatedDay.Mirror(V1().Bar("11:30", 113.75m, 116.25m, 113.75m, 116m).Build(), 110.5m);

        var r = VwapPullbackReader.Read(Prefix(21, up: false), mirrored.Bars, Strategy, ReaderSide.Sell);

        r.ShouldNotBeNull();
        r.EntryPrice.ShouldBe(107.25m);
        r.Stop.ShouldBe(108.5m);
        r.Target.ShouldBe(104.75m);
        r.ExitPrice.ShouldBe(104.75m);
        r.ExitReason.ShouldBe(ReaderExitReason.Target);
        r.SeenR.ShouldBe(2m);
    }

    [Fact]
    public void V08_No_buy_in_a_down_trend()
    {
        // the daily close 110.50 is not above its EMA(20) 115.25 and the hourly EMA(20) is below EMA(50)
        VwapPullbackReader.Read(Prefix(21, up: false), V1().Bars(), Strategy, ReaderSide.Buy).ShouldBeNull();
    }

    [Fact]
    public void V09_No_trade_when_the_trend_averages_are_not_ready()
    {
        // 5 sessions: 5 daily bars and 35 hourly bars are too few
        Buy(V1(5), 5).ShouldBeNull();
    }

    [Fact]
    public void V10_No_trade_when_the_dip_is_too_far_from_vwap()
    {
        // pull_atr 0.05: 112.50 is above vwap 112.3667 + 0.05 x 1.00
        Buy(V1(), strategy: Strategy with { PullAtr = 0.05m }).ShouldBeNull();
    }

    [Fact]
    public void V11_No_trade_when_the_trigger_comes_before_the_first_entry()
    {
        // the same six bars shifted: the same pattern decided at 09:30 to 10:45 is fine, but a pattern that completes at bar 2 (decided 09:45) cannot fire: bar 1 has no lag
        var day = new DatedDay(FixtureDate(21))
            .Bar("09:15", 110.5m, 111.5m, 110.5m, 111.25m)
            .Bar("09:30", 111.25m, 112.25m, 111.25m, 112m);

        Buy(day).ShouldBeNull();
    }

    [Fact]
    public void V12_The_first_candidate_is_the_only_one()
    {
        // a miss of the first candidate (no touch of the limit) is final even if a later bar triggers again
        var day = V1().Bar("10:45", 114m, 114.5m, 113.9m, 114m).Bar("11:00", 114m, 115m, 113.5m, 115m);

        Buy(day).ShouldBeNull();
    }

    [Fact]
    public void V13_A_trigger_after_14_30_is_ignored()
    {
        // V1's six bars moved to start at 13:00: the trigger bar closes at 14:30 + 15 = 14:45 decided, after the last entry 14:30
        var day = new DatedDay(FixtureDate(21)).Flat("09:15", 110.5m);
        var bars = new (string At, decimal O, decimal H, decimal L, decimal C)[]
        {
            ("13:30", 110.5m, 111.5m, 110.5m, 111.25m),
            ("13:45", 111.25m, 112.25m, 111.25m, 112m),
            ("14:00", 112m, 113m, 112m, 112.75m),
            ("14:15", 112.75m, 113.75m, 112.75m, 113.5m),
            ("14:30", 113.5m, 113.5m, 112.5m, 113m),
            ("14:45", 113m, 114m, 113m, 113.75m),
        };
        foreach (var (at, o, h, l, c) in bars)
        {
            day.Bar(at, o, h, l, c);
        }

        Buy(day).ShouldBeNull();
    }

    [Fact]
    public void V14_The_reader_refuses_a_bad_strategy_and_other_dates()
    {
        Should.Throw<ArgumentException>(() => VwapPullbackReader.Read([], V1().Bars(), Strategy with { PullAtr = 0m }, ReaderSide.Buy));
        Should.Throw<ArgumentException>(() => VwapPullbackReader.Read([], [.. V1().Bars(), .. new DatedDay(new DateOnly(2030, 1, 1)).Flat("09:15", 1m).Bars()], Strategy, ReaderSide.Buy));
    }
}
