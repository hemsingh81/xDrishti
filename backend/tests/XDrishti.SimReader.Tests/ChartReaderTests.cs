namespace XDrishti.SimReader.Tests;

/// <summary>
/// Hand-computed days (spec P3-09c AC 1). Common frame: 2026-03-02, tick 0.05, ORB v1 (or_min 15, rr 2, window 09:30 to 11:30 on the decision time, square-off 15:15, limit 0.1 %).
/// Opening range: 09:20 high 101 and 09:25 low 99, so OR high 101, OR low 99, everything else flat 100 to 09:43.
/// BUY day: 09:30 bar closes 102.5; limit = tickDown(102.5 x 1.001 = 102.6025) = 102.60; entry 102.5 at the 09:45 open; stop 99; risk 3.5; target 102.5 + 2 x 3.5 = 109.5.
/// SELL day: 09:30 bar closes 97.5; limit = tickUp(97.5 x 0.999 = 97.4025) = 97.45; entry 97.5; stop 101; risk 3.5; target 97.5 - 7 = 90.5.
/// </summary>
public sealed class ChartReaderTests
{
    private static ReaderStrategy Orb(decimal tick = 0.05m) => ReaderStrategy.OrbV1(tick);

    private static ReaderReading? Buy(Day d, decimal tick = 0.05m) => ChartReader.Read(d.Build(), Orb(tick), ReaderSide.Buy);

    private static ReaderReading? Sell(Day d) => ChartReader.Read(d.Build(), Orb(), ReaderSide.Sell);

    private static void Exit(ReaderReading? r, string at, decimal price, ReaderExitReason reason, decimal seenR)
    {
        r.ShouldNotBeNull();
        r.ExitEpoch.ShouldBe(Day.Epoch(at));
        r.ExitPrice.ShouldBe(price);
        r.ExitReason.ShouldBe(reason);
        r.SeenR.ShouldBe(seenR);
    }

    private static void Entry(ReaderReading? r, string at, decimal price, decimal stop, decimal target, bool atLimit = false)
    {
        r.ShouldNotBeNull();
        r.EntryEpoch.ShouldBe(Day.Epoch(at));
        r.EntryPrice.ShouldBe(price);
        r.Stop.ShouldBe(stop);
        r.Target.ShouldBe(target);
        r.EntryAtLimit.ShouldBe(atLimit);
    }

    [Fact]
    public void T01_Buy_reaches_the_target()
    {
        // 10:30 bar high 109.5 >= target 102.5 + 2 x 3.5 = 109.5 (touch counts); low 102.5 > stop 99. Exit at the target. R = 7 / 3.5 = 2.
        var r = Buy(Day.Buy().Bar("10:30", 102.5m, 109.5m, 102.5m, 109m));
        r.ShouldNotBeNull();
        r.OpeningRangeHigh.ShouldBe(101m);
        r.OpeningRangeLow.ShouldBe(99m);
        r.TriggerBarStartEpoch.ShouldBe(Day.Epoch("09:30"));
        r.TriggerClose.ShouldBe(102.5m);
        Entry(r, "09:45", 102.5m, 99m, 109.5m);
        Exit(r, "10:30", 109.5m, ReaderExitReason.Target, 2m);
    }

    [Fact]
    public void T02_Sell_reaches_the_target()
    {
        // SELL: 97.5 < OR low 99, previous bar closed 100 >= 99. Stop = OR high 101, risk 3.5, target 97.5 - 7 = 90.5; 10:00 low 90.5 touches it. R = (97.5 - 90.5) / 3.5 = 2.
        var r = Sell(Day.Sell().Bar("10:00", 97.5m, 97.5m, 90.5m, 91m));
        Entry(r, "09:45", 97.5m, 101m, 90.5m);
        Exit(r, "10:00", 90.5m, ReaderExitReason.Target, 2m);
    }

    [Fact]
    public void T03_Buy_stopped_out()
    {
        // 10:00 low 98.5 <= stop 99, open 102.5 above it: exit at min(99, 102.5) = 99. R = (99 - 102.5) / 3.5 = -1.
        var r = Buy(Day.Buy().Bar("10:00", 102.5m, 102.5m, 98.5m, 99m));
        Exit(r, "10:00", 99m, ReaderExitReason.Stop, -1m);
    }

    [Fact]
    public void T04_Buy_gap_through_the_stop_fills_at_the_open()
    {
        // 10:00 opens 97 < stop 99: exit at min(99, 97) = 97. R = (97 - 102.5) / 3.5 = -5.5 / 3.5 = -1.571429 (to 6 places).
        var r = Buy(Day.Buy().Bar("10:00", 97m, 98m, 96m, 97.5m));
        Exit(r, "10:00", 97m, ReaderExitReason.Stop, -1.571429m);
    }

    [Fact]
    public void T05_Stop_in_the_entry_minute()
    {
        // Fill minute 09:45: open 102.5 <= limit 102.60 so entry 102.5; its low 98.5 <= 99 is a stop; high 103 < target 109.5. Exit 09:45 at 99, R = -1.
        var r = Buy(Day.Buy().Bar("09:45", 102.5m, 103m, 98.5m, 99m));
        Entry(r, "09:45", 102.5m, 99m, 109.5m);
        Exit(r, "09:45", 99m, ReaderExitReason.Stop, -1m);
    }

    [Fact]
    public void T06_A_minute_holding_stop_and_target_is_a_stop()
    {
        // 10:00 (open 102.5, high 110 >= 109.5, low 98 <= 99). The open is between the levels, so stop first at 99. R = -1.
        var r = Buy(Day.Buy().Bar("10:00", 102.5m, 110m, 98m, 100m));
        Exit(r, "10:00", 99m, ReaderExitReason.Stop, -1m);
    }

    [Fact]
    public void T07_Open_beyond_the_target_is_a_target_even_when_the_low_touches_the_stop()
    {
        // 10:00 opens 110 >= 109.5 (first print is beyond the target), low 98 <= 99: ADR 0038 X4 gives the target at 109.5. R = 2.
        var r = Buy(Day.Buy().Bar("10:00", 110m, 112m, 98m, 100m));
        Exit(r, "10:00", 109.5m, ReaderExitReason.Target, 2m);
    }

    [Fact]
    public void T08_Gap_through_the_target_never_fills_better_than_the_target()
    {
        // 10:00 opens 111 and stays above: the target fills at 109.5, not 111. R = 2.
        var r = Buy(Day.Buy().Bar("10:00", 111m, 112m, 110m, 111m));
        Exit(r, "10:00", 109.5m, ReaderExitReason.Target, 2m);
    }

    [Fact]
    public void T09_Square_off_at_the_open_of_15_15()
    {
        // Flat 102.5 to 15:14; 15:15 opens 102.0 inside (99, 109.5): square-off at its open 102.0 (its range is not used). R = (102 - 102.5) / 3.5 = -0.142857.
        var r = Buy(Day.Buy().Bar("15:15", 102m, 105m, 101m, 103m));
        Exit(r, "15:15", 102m, ReaderExitReason.SquareOff, -0.142857m);
    }

    [Fact]
    public void T10_Square_off_waits_for_the_next_minute_with_a_trade()
    {
        // 15:15 and 15:16 are absent; 15:17 opens 101: exit at 15:17 open 101. R = (101 - 102.5) / 3.5 = -0.428571.
        var r = Buy(Day.Buy().Remove("15:15", "15:16").Bar("15:17", 101m, 102m, 100m, 101m));
        Exit(r, "15:17", 101m, ReaderExitReason.SquareOff, -0.428571m);
    }

    [Fact]
    public void T11_A_zero_volume_square_off_minute_holds_no_trade()
    {
        // 15:15 has volume 0 (open 90 would be beyond the stop but holds no trade); 15:16 opens 103: exit there. R = 0.5 / 3.5 = 0.142857.
        var r = Buy(Day.Buy().Bar("15:15", 90m, 90m, 90m, 90m, 0m).Bar("15:16", 103m, 103m, 103m, 103m));
        Exit(r, "15:16", 103m, ReaderExitReason.SquareOff, 0.142857m);
    }

    [Fact]
    public void T12_Square_off_minute_that_opens_beyond_the_stop_is_a_stop()
    {
        // 15:15 opens 98 <= stop 99: X1 gives Stop at the open 98. R = (98 - 102.5) / 3.5 = -1.285714.
        var r = Buy(Day.Buy().Bar("15:15", 98m, 99m, 97m, 98m));
        Exit(r, "15:15", 98m, ReaderExitReason.Stop, -1.285714m);
    }

    [Fact]
    public void T13_Square_off_minute_that_opens_beyond_the_target_exits_at_the_target()
    {
        // 15:15 opens 110 >= 109.5: X1 gives Target at 109.5. R = 2.
        var r = Buy(Day.Buy().Bar("15:15", 110m, 111m, 110m, 110m));
        Exit(r, "15:15", 109.5m, ReaderExitReason.Target, 2m);
    }

    [Fact]
    public void T14_End_of_data_leaves_at_the_last_close()
    {
        // The data ends at 14:00 with close 103: EndOfData at that close, exit minute 14:00. R = 0.5 / 3.5 = 0.142857.
        var r = Buy(Day.Buy().Remove("14:01", "15:29").Bar("14:00", 102.5m, 103m, 102.5m, 103m));
        Exit(r, "14:00", 103m, ReaderExitReason.EndOfData, 0.142857m);
    }

    [Fact]
    public void T15_Missing_minutes_after_the_trigger_delay_the_entry()
    {
        // 09:45 and 09:46 are missing. Decision instant 09:45; fill minute 09:47 starts 2 minutes later (<= 5): entry 102.5 at its open. Target at 10:30 as T01.
        var r = Buy(Day.Buy().Remove("09:45", "09:46").Bar("10:30", 102.5m, 110m, 102.5m, 109m));
        Entry(r, "09:47", 102.5m, 99m, 109.5m);
        Exit(r, "10:30", 109.5m, ReaderExitReason.Target, 2m);
    }

    [Fact]
    public void T16_A_bucket_with_its_last_minute_missing_closes_late()
    {
        // 09:44 is missing, so the 09:30 bar (close = close of 09:43 = 102.5) closes when the 09:45 minute arrives; decision instant 09:46.
        // Fill minute = the next minute after 09:45 = 09:46 (delay 0): entry 102.5, same levels.
        var d = Day.WithRange().Bar("09:43", 100m, 103m, 100m, 102.5m).Remove("09:44", "09:44").Flat("09:45", "15:29", 102.5m);
        var r = Buy(d.Bar("10:30", 102.5m, 109.5m, 102.5m, 109m));
        r.ShouldNotBeNull();
        r.TriggerBarStartEpoch.ShouldBe(Day.Epoch("09:30"));
        r.TriggerClose.ShouldBe(102.5m);
        Entry(r, "09:46", 102.5m, 99m, 109.5m);
        Exit(r, "10:30", 109.5m, ReaderExitReason.Target, 2m);
    }

    [Fact]
    public void T17_An_empty_opening_range_gives_no_trade()
    {
        // The stream starts at 10:00: no minute in 09:15 to 09:30, so no OR and no trade.
        var d = new Day().Flat("10:00", "15:29", 100m).Bar("10:14", 100m, 103m, 100m, 103m);
        Buy(d).ShouldBeNull();
    }

    [Fact]
    public void T18_A_trigger_deciding_at_the_last_entry_time_is_taken()
    {
        // Flat 100 to 11:28; the 11:15 bar closes 102.5 (11:29 minute) > 101 with the 11:00 bar at 100 <= 101: decision 11:30 (inclusive).
        // Fill 11:30 open 102.5; entry 102.5, stop 99, target 109.5; 11:45 high 109.5 touches it.
        var d = Day.WithRange().Flat("09:44", "11:28", 100m).Bar("11:29", 100m, 103m, 100m, 102.5m).Flat("11:30", "15:29", 102.5m).Bar("11:45", 102.5m, 109.5m, 102.5m, 109m);
        var r = Buy(d);
        r.ShouldNotBeNull();
        r.TriggerBarStartEpoch.ShouldBe(Day.Epoch("11:15"));
        Entry(r, "11:30", 102.5m, 99m, 109.5m);
        Exit(r, "11:45", 109.5m, ReaderExitReason.Target, 2m);
    }

    [Fact]
    public void T19_A_trigger_deciding_after_the_last_entry_time_gives_no_trade()
    {
        // The break is in the 11:30 bar: it closes at 11:45 > 11:30, outside the window. No trade.
        var d = Day.WithRange().Flat("09:44", "11:43", 100m).Bar("11:44", 100m, 103m, 100m, 102.5m).Flat("11:45", "15:29", 102.5m);
        Buy(d).ShouldBeNull();
    }

    [Fact]
    public void T20_Entry_beyond_the_limit_is_no_trade()
    {
        // Fill minute 09:45 opens 103 > limit 102.60 and its low 102.9 > 102.60: the order does not fill (final). No trade.
        var d = Day.Buy().Bar("09:45", 103m, 103.5m, 102.9m, 103m);
        Buy(d).ShouldBeNull();
    }

    [Fact]
    public void T21_Entry_at_the_limit_when_the_minute_trades_back_to_it()
    {
        // 09:45 opens 103 > 102.60 but its low 102.6 <= 102.60: filled at the limit 102.60. Risk 3.6, target 102.6 + 7.2 = 109.8.
        // Flat 103 afterwards (inside 99..109.8): square-off at 15:15 open 103. R = 0.4 / 3.6 = 0.111111.
        var d = Day.Buy().Bar("09:45", 103m, 103.2m, 102.6m, 103m).Flat("09:46", "15:29", 103m);
        var r = Buy(d);
        Entry(r, "09:45", 102.6m, 99m, 109.8m, atLimit: true);
        Exit(r, "15:15", 103m, ReaderExitReason.SquareOff, 0.111111m);
    }

    [Fact]
    public void T22_The_target_is_not_checked_in_the_fill_minute_of_a_limit_entry()
    {
        // As T21 but the 09:45 minute also reaches 110 (>= 109.8): X5 checks only the stop in that minute, so no target there (the high may have come before the fill).
        // Then flat 109 (< 109.8) to the square-off: SquareOff at 109. R = (109 - 102.6) / 3.6 = 1.777778.
        var d = Day.Buy().Bar("09:45", 103m, 110m, 102.6m, 109m).Flat("09:46", "15:29", 109m);
        var r = Buy(d);
        Entry(r, "09:45", 102.6m, 99m, 109.8m, atLimit: true);
        Exit(r, "15:15", 109m, ReaderExitReason.SquareOff, 1.777778m);
    }

    [Fact]
    public void T23_Zero_volume_minutes_hold_no_trade_at_the_entry_and_in_the_walk()
    {
        // 09:45 has volume 0 and an open of 90: skipped; the fill minute is 09:46 (delay 60 s): entry 102.5. 10:00 has volume 0 with low 90 (would be a stop): ignored.
        // 10:30 touches the target 109.5. R = 2.
        var d = Day.Buy().Bar("09:45", 90m, 90m, 90m, 90m, 0m).Bar("10:00", 102.5m, 102.5m, 90m, 102.5m, 0m).Bar("10:30", 102.5m, 109.5m, 102.5m, 109m);
        var r = Buy(d);
        Entry(r, "09:46", 102.5m, 99m, 109.5m);
        Exit(r, "10:30", 109.5m, ReaderExitReason.Target, 2m);
    }

    [Fact]
    public void T24_A_fill_minute_more_than_five_minutes_after_the_decision_is_no_trade()
    {
        // Decision instant 09:45. 09:45 to 09:50 missing: the fill minute 09:51 starts 6 minutes after: no trade.
        Buy(Day.Buy().Remove("09:45", "09:50")).ShouldBeNull();
    }

    [Fact]
    public void T25_A_fill_minute_exactly_five_minutes_after_the_decision_is_allowed()
    {
        // 09:45 to 09:49 missing: the fill minute 09:50 starts exactly 5 minutes after the decision instant: allowed. Entry 102.5.
        var r = Buy(Day.Buy().Remove("09:45", "09:49").Bar("10:30", 102.5m, 109.5m, 102.5m, 109m));
        Entry(r, "09:50", 102.5m, 99m, 109.5m);
    }

    [Fact]
    public void T26_A_fill_at_or_through_the_stop_is_no_trade()
    {
        // 09:45 opens 98 <= limit, so entry would be 98 <= stop 99: no trade.
        Buy(Day.Buy().Bar("09:45", 98m, 98.5m, 97m, 98m)).ShouldBeNull();
    }

    [Fact]
    public void T27_The_first_trigger_spends_the_ticket()
    {
        // Bar 09:30 closes 102.5 (trigger) and the order is not reached (09:45 opens 103, low 102.9). Bar 09:45 closes 100; bar 10:00 closes 102.5 > 101 with previous 100 <= 101: a second break, but only the first is a candidate. No trade.
        var d = Day.Buy().Bar("09:45", 103m, 103.5m, 102.9m, 103m).Flat("09:46", "09:59", 100m).Flat("10:00", "15:29", 102.5m);
        Buy(d).ShouldBeNull();
    }

    [Fact]
    public void T28_A_close_equal_to_the_range_high_is_not_a_break()
    {
        // 09:30 bar closes 101 = OR high 101: close > or_high is false. No trade.
        var d = Day.WithRange().Bar("09:44", 100m, 101m, 100m, 101m).Flat("09:45", "15:29", 101m);
        Buy(d).ShouldBeNull();
    }

    [Fact]
    public void T29_Sell_gap_through_the_stop_fills_at_the_open()
    {
        // SELL stop 101; 10:00 opens 104: exit at max(101, 104) = 104. R = -(104 - 97.5) / 3.5 = -1.857143.
        var r = Sell(Day.Sell().Bar("10:00", 104m, 105m, 103.5m, 104m));
        Exit(r, "10:00", 104m, ReaderExitReason.Stop, -1.857143m);
    }

    [Fact]
    public void T30_Sell_entry_at_the_limit_rounds_the_limit_up()
    {
        // limit = tickUp(97.4025) = 97.45. 09:45 (97.4, 97.6, 97.4, 97.5): open 97.4 < 97.45 so beyond the limit; high 97.6 >= 97.45: filled at 97.45.
        // Risk 101 - 97.45 = 3.55; target 97.45 - 7.1 = 90.35. Flat 97.5 then (stop 101 not reached) square-off 15:15 at 97.5: R = -(97.5 - 97.45) / 3.55 = -0.014085.
        var d = Day.Sell().Bar("09:45", 97.4m, 97.6m, 97.4m, 97.5m);
        var r = Sell(d);
        Entry(r, "09:45", 97.45m, 101m, 90.35m, atLimit: true);
        Exit(r, "15:15", 97.5m, ReaderExitReason.SquareOff, -0.014085m);
    }

    [Fact]
    public void T31_A_missing_first_minute_leaves_the_range_to_the_minutes_present()
    {
        // 09:15 missing: OR from the minutes present (09:20 high 101, 09:25 low 99): same range, same trade as T01.
        var r = Buy(Day.Buy().Remove("09:15", "09:15").Bar("10:30", 102.5m, 109.5m, 102.5m, 109m));
        r.ShouldNotBeNull();
        r.OpeningRangeHigh.ShouldBe(101m);
        r.OpeningRangeLow.ShouldBe(99m);
        Exit(r, "10:30", 109.5m, ReaderExitReason.Target, 2m);
    }

    [Fact]
    public void T32_The_limit_is_rounded_to_the_instruments_tick()
    {
        // Tick 0.25: limit = tickDown(102.6025) = floor(410.41) x 0.25 = 102.50. 09:45 opens 102.55 > 102.50 and its low 102.5 <= 102.50: filled at the limit 102.50.
        // Target 102.5 + 7 = 109.5; flat 102.55 afterwards: square-off at the 15:15 open 102.55, R = 0.05 / 3.5 = 0.014286.
        var d = Day.Buy().Bar("09:45", 102.55m, 102.6m, 102.5m, 102.55m).Flat("09:46", "15:29", 102.55m);
        var r = Buy(d, 0.25m);
        Entry(r, "09:45", 102.5m, 99m, 109.5m, atLimit: true);
        Exit(r, "15:15", 102.55m, ReaderExitReason.SquareOff, 0.014286m);
    }

    [Fact]
    public void T33_A_decision_at_or_after_the_square_off_gives_no_fill()
    {
        // Square-off set to 09:45: the fill minute (09:45) starts at or after it, so no trade (AfterSquareOff).
        var strategy = Orb(0.05m) with { SquareOff = new TimeOnly(9, 45) };
        ChartReader.Read(Day.Buy().Build(), strategy, ReaderSide.Buy).ShouldBeNull();
    }

    [Fact]
    public void T34_Several_days_or_unsorted_bars_are_a_caller_error()
    {
        var one = Day.Buy().Build();
        Should.Throw<ArgumentException>(() => ChartReader.Read([one[1], one[0]], Orb(), ReaderSide.Buy));
        var next = one[0] with { EpochSeconds = one[0].EpochSeconds + 86400 };
        Should.Throw<ArgumentException>(() => ChartReader.Read([one[0], next], Orb(), ReaderSide.Buy));
    }

    [Fact]
    public void T35_No_break_on_the_other_side_gives_no_trade()
    {
        // The BUY day does not break below the OR low 99: the SELL reading finds nothing.
        Sell(Day.Buy()).ShouldBeNull();
    }
}
