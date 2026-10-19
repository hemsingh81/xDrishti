using static XDrishti.Simulation.Tests.Oracle.Fx;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>A micro-test per rule row (E0 to E9, X1 to X9, S1 to S5, D11) and per rule of ADR 0047 (R1 to R13) on small hand-made lists.</summary>
public sealed class RuleRowTests
{
    private static readonly OracleSlippage Default = OracleSlippage.Default;

    // BUY, tick 0.05, trigger close 100.00 (limit 100.10), stop 99.00, k 2 (target 102.00), flat 100.00 unless listed.
    private static OracleResult Run(
        IEnumerable<Mn> listed,
        string[]? absent = null,
        OracleSlippage? slip = null,
        OracleSide side = OracleSide.Buy,
        decimal start = 100.00m,
        decimal trig = 100.00m,
        decimal stop = 99.00m,
        string decision = "10:00",
        double? rr = 2.0,
        string to = "15:29",
        decimal tick = 0.05m)
    {
        Tape t = new Tape().Day(Mon, "09:15", to, start, listed, absent ?? []);
        OracleCandidate c = Cand(t, side, Mon, decision, trig, stop);
        return ReferenceWalker.Walk(t.ToArray(), c, Intraday(slip ?? OracleSlippage.None, tick, rr), OracleFaults.None);
    }

    private static OracleResult RunSwing(Tape t, OracleCandidate c, int n, OracleCalendar? cal = null, OracleSlippage? slip = null) =>
        ReferenceWalker.Walk(t.ToArray(), c, Swing(n, cal, slip ?? OracleSlippage.None), OracleFaults.None);

    // ---- E0 ----
    [Fact]
    public void E0_a_zero_volume_minute_is_passed_over_in_the_search_and_counted()
    {
        OracleResult r = Run([V0("10:00", 100.00m, 100.00m, 100.00m, 100.00m)]);
        r.Trade!.EntryEpochSeconds.ShouldBe(E(Mon, "10:01"));
        r.Counters.ZeroVolumeMinutes.ShouldBe(1);
        r.Counters.EntriesAfterMissingMinutes.ShouldBe(1);
        r.Rows.ShouldContain(OracleRows.E0);
    }

    [Fact]
    public void E0_a_zero_volume_minute_inside_the_position_tests_no_stop_and_counts_nowhere()
    {
        OracleResult r = Run([V0("10:30", 100.00m, 100.00m, 90.00m, 100.00m)]);
        r.Trade!.ExitReason.ShouldBe(OracleExitReason.SquareOff);
        r.Trade.MaeR.ShouldBe(0.0);
        r.Counters.ZeroVolumeMinutes.ShouldBe(1);
    }

    // ---- E1 ----
    [Fact]
    public void E1_the_fill_minute_is_the_first_minute_after_the_candidates_own()
    {
        Run([]).Trade!.EntryEpochSeconds.ShouldBe(E(Mon, "10:00"));
    }

    [Fact]
    public void E1_five_minutes_after_the_decision_instant_fills_and_six_is_no_fill_bar()
    {
        Run([], absent: ["10:00-10:04"]).Trade!.EntryEpochSeconds.ShouldBe(E(Mon, "10:05"));
        Run([], absent: ["10:00-10:05"]).NoTrade.ShouldBe(OracleNoTradeReason.NoFillBar);
    }

    [Fact]
    public void E1_a_fill_minute_at_or_after_the_square_off_is_after_square_off_and_a_list_end_is_no_fill_bar()
    {
        Run([], decision: "15:15").NoTrade.ShouldBe(OracleNoTradeReason.AfterSquareOff);
        Run([], to: "09:59").NoTrade.ShouldBe(OracleNoTradeReason.NoFillBar);
    }

    // ---- E2 ----
    [Fact]
    public void E2_the_buy_limit_is_rounded_down_and_the_sell_limit_up()
    {
        // 100.40 x 1.001 = 100.5004 -> 100.50: an open of 100.55 is not within it.
        Run([M("10:00", 100.55m, 100.70m, 100.55m, 100.60m)], trig: 100.40m, stop: 99.00m).NoTrade.ShouldBe(OracleNoTradeReason.LimitNotReached);
        Run([M("10:00", 100.50m, 100.70m, 100.50m, 100.60m)], trig: 100.40m, stop: 99.00m).Trade!.EntryPrice.ShouldBe(100.50);

        // 100.40 x 0.999 = 100.2996 -> 100.30 (up): an open of 100.25 is below it and its high does not reach it.
        Run([M("10:00", 100.25m, 100.25m, 100.10m, 100.20m)], side: OracleSide.Sell, trig: 100.40m, stop: 102.00m).NoTrade.ShouldBe(OracleNoTradeReason.LimitNotReached);
        Run([M("10:00", 100.30m, 100.40m, 100.20m, 100.30m)], side: OracleSide.Sell, trig: 100.40m, stop: 102.00m).Trade!.EntryPrice.ShouldBe(100.30);
    }

    // ---- E3, E4, E5, E6 ----
    [Fact]
    public void E3_an_open_equal_to_the_limit_fills_at_the_open()
    {
        OracleResult r = Run([M("10:00", 100.10m, 100.20m, 100.00m, 100.10m)]);
        r.Trade!.EntryFill.ShouldBe(OracleEntryFill.AtOpen);
        r.Trade.EntryPrice.ShouldBe(100.10);
        r.Rows.ShouldContain(OracleRows.E3);
        r.Rows.ShouldContain(OracleRows.TieLimit);
    }

    [Fact]
    public void E4_a_low_equal_to_the_limit_fills_at_the_limit_and_the_sell_mirrors_it()
    {
        OracleResult r = Run([M("10:00", 100.30m, 100.40m, 100.10m, 100.30m)]);
        r.Trade!.EntryFill.ShouldBe(OracleEntryFill.AtLimit);
        r.Trade.EntryPrice.ShouldBe(100.10);
        r.Rows.ShouldContain(OracleRows.E4);

        // Sell: limit tickUp(100.00 x 0.999 = 99.90) = 99.90; open 99.80 below it, high 99.90 touches it.
        OracleResult s = Run([M("10:00", 99.80m, 99.90m, 99.70m, 99.80m)], side: OracleSide.Sell, stop: 101.00m);
        s.Trade!.EntryFill.ShouldBe(OracleEntryFill.AtLimit);
        s.Trade.EntryPrice.ShouldBe(99.90);
    }

    [Fact]
    public void E5_one_tick_short_of_the_limit_is_a_miss_that_is_final()
    {
        OracleResult r = Run([M("10:00", 100.15m, 100.20m, 100.15m, 100.15m), M("10:01", 100.00m, 100.10m, 99.90m, 100.00m)]);
        r.NoTrade.ShouldBe(OracleNoTradeReason.LimitNotReached);
        r.Counters.BarsExamined.ShouldBe(1);
        r.Rows.ShouldContain(OracleRows.E5);
    }

    [Fact]
    public void E6_an_entry_at_or_through_the_stop_is_no_trade_and_a_sell_mirrors_it()
    {
        Run([M("10:00", 99.00m, 99.10m, 98.90m, 99.00m)]).NoTrade.ShouldBe(OracleNoTradeReason.EntryThroughStop);
        Run([M("10:00", 101.00m, 101.10m, 100.90m, 101.00m)], side: OracleSide.Sell, stop: 101.00m).NoTrade.ShouldBe(OracleNoTradeReason.EntryThroughStop);
        Run([M("10:00", 99.05m, 99.10m, 98.90m, 99.05m)]).Trade.ShouldNotBeNull();
    }

    // ---- E8, E9 ----
    [Fact]
    public void E8_a_swing_decision_at_the_end_of_the_session_rests_overnight_and_an_intraday_one_does_not()
    {
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m).Day(Tue, "09:15", "15:29");
        OracleCandidate c = Cand(t, OracleSide.Buy, Mon, "15:30", 100.00m, 95.00m);
        OracleResult r = RunSwing(t, c, 2);
        r.Trade!.TradeDate.ShouldBe(D(Tue));
        r.Rows.ShouldContain(OracleRows.E8);
        ReferenceWalker.Walk(t.ToArray(), c, Intraday(OracleSlippage.None), OracleFaults.None).NoTrade.ShouldBe(OracleNoTradeReason.NoFillBar);

        // A swing decision inside the session is not an overnight order.
        OracleCandidate mid = Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 95.00m);
        RunSwing(t, mid, 2).Rows.ShouldNotContain(OracleRows.E8);
    }

    [Fact]
    public void E8_the_end_of_a_special_session_comes_from_the_calendar()
    {
        OracleCalendar cal = Cal([], (Mon, "09:15", "12:30"));
        Tape t = new Tape().Day(Mon, "09:15", "12:29", 100.00m).Day(Tue, "09:15", "15:29");
        OracleCandidate c = Cand(t, OracleSide.Buy, Mon, "12:30", 100.00m, 95.00m);
        RunSwing(t, c, 2, cal).Rows.ShouldContain(OracleRows.E8);
    }

    [Fact]
    public void E9_the_fill_session_skips_the_weekend_and_rests_over_a_holiday()
    {
        Tape fri = new Tape().Day(Fri, "09:15", "15:29", 100.00m).Day(Sat, "09:15", "15:29").Day(Mon2, "09:15", "15:29");
        OracleResult r = RunSwing(fri, Cand(fri, OracleSide.Buy, Fri, "15:30", 100.00m, 95.00m), 2);
        // The Saturday bars are on another date that is not the fill session: the later session is never used.
        r.NoTrade.ShouldBe(OracleNoTradeReason.NoFillBar);

        Tape ok = new Tape().Day(Fri, "09:15", "15:29", 100.00m).Day(Mon2, "09:15", "15:29");
        OracleResult w = RunSwing(ok, Cand(ok, OracleSide.Buy, Fri, "15:30", 100.00m, 95.00m), 2);
        w.Trade!.TradeDate.ShouldBe(D(Mon2));
        w.Rows.ShouldContain(OracleRows.E9Weekend);

        Tape hol = new Tape().Day(Mon, "09:15", "15:29", 100.00m).Day(Wed, "09:15", "15:29");
        OracleResult h = RunSwing(hol, Cand(hol, OracleSide.Buy, Mon, "15:30", 100.00m, 95.00m), 2, Cal([Tue]));
        h.Trade!.TradeDate.ShouldBe(D(Wed));
        h.Rows.ShouldContain(OracleRows.E9Holiday);
    }

    [Fact]
    public void E9_minutes_of_the_decision_date_after_the_candidates_own_are_ignored_and_before_the_open_are_skipped()
    {
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m).Day(Tue, "09:15", "15:29");
        // A candidate whose own minute is 15:20: the minutes 15:21 to 15:29 of the decision date are ignored without a count.
        OracleCandidate c = Cand(t, OracleSide.Buy, Mon, "15:21", 100.00m, 95.00m);
        OracleResult r = ReferenceWalker.Walk(t.ToArray(), c, Swing(2) with { Hold = new OracleHold(2, Cal([], (Mon, "09:15", "15:21"))) }, OracleFaults.None);
        r.Trade!.TradeDate.ShouldBe(D(Tue));
        r.Counters.ZeroVolumeMinutes.ShouldBe(0);
    }

    // ---- X1 ----
    [Fact]
    public void X1_the_square_off_minute_exits_at_its_open_with_the_reason_of_the_level_the_open_is_beyond()
    {
        OracleResult stop = Run([M("15:15", 98.90m, 99.00m, 98.80m, 98.85m)]);
        stop.Trade!.ExitReason.ShouldBe(OracleExitReason.Stop);
        stop.Trade.ExitPrice.ShouldBe(98.90);
        OracleResult tgt = Run([M("15:15", 102.50m, 103.00m, 102.40m, 102.60m)]);
        tgt.Trade!.ExitReason.ShouldBe(OracleExitReason.Target);
        tgt.Trade.ExitPrice.ShouldBe(102.0);
        OracleResult sq = Run([M("15:15", 100.40m, 110.00m, 90.00m, 100.00m)]);
        sq.Trade!.ExitReason.ShouldBe(OracleExitReason.SquareOff);
        sq.Trade.ExitPrice.ShouldBe(100.40);
        sq.Rows.ShouldContain(OracleRows.X1);
    }

    [Fact]
    public void X1_the_next_minute_that_holds_a_trade_decides_and_a_late_square_off_is_counted()
    {
        OracleResult r = Run([V0("15:15", 100.00m, 100.00m, 100.00m, 100.00m), M("15:17", 100.30m, 100.40m, 100.20m, 100.30m)], absent: ["15:16"]);
        r.Trade!.ExitEpochSeconds.ShouldBe(E(Mon, "15:17"));
        r.Counters.LateSquareOffs.ShouldBe(1);
        r.Rows.ShouldContain(OracleRows.X1Late);
    }

    // ---- X2, X3, X4, X5, X6 ----
    [Fact]
    public void X2_a_stop_exits_at_the_stop_or_at_the_open_when_the_open_is_beyond_it_and_the_sell_mirrors_it()
    {
        Run([M("10:30", 99.50m, 99.60m, 98.90m, 99.00m)]).Trade!.ExitPrice.ShouldBe(99.00);
        OracleResult gap = Run([M("10:30", 98.40m, 98.60m, 98.30m, 98.50m)]);
        gap.Trade!.ExitPrice.ShouldBe(98.40);
        gap.Counters.StopGapExits.ShouldBe(1);
        OracleResult sell = Run([M("10:30", 101.30m, 101.40m, 101.00m, 101.20m)], side: OracleSide.Sell, stop: 101.00m, trig: 100.00m);
        sell.Trade!.ExitPrice.ShouldBe(101.30);
    }

    [Fact]
    public void X3_a_target_exits_at_the_target_never_at_a_better_price()
    {
        OracleResult r = Run([M("10:30", 103.00m, 103.50m, 102.50m, 103.20m)]);
        r.Trade!.ExitPrice.ShouldBe(102.0);
        r.Counters.TargetGapExits.ShouldBe(1);
        r.Trade.RExact.ShouldBe(2.0);
    }

    [Fact]
    public void X4_a_minute_that_reaches_both_is_a_stop_unless_the_open_is_beyond_the_target_and_it_is_ambiguous_only_when_the_open_is_strictly_between()
    {
        OracleResult between = Run([M("10:30", 100.50m, 102.50m, 98.50m, 100.00m)]);
        between.Trade!.ExitReason.ShouldBe(OracleExitReason.Stop);
        between.Counters.AmbiguousMinutes.ShouldBe(1);

        OracleResult beyond = Run([M("10:30", 102.00m, 102.50m, 98.50m, 100.00m)]);
        beyond.Trade!.ExitReason.ShouldBe(OracleExitReason.Target);
        beyond.Counters.AmbiguousMinutes.ShouldBe(0);
        beyond.Counters.TargetGapExits.ShouldBe(0);
        beyond.Rows.ShouldContain(OracleRows.X4Beyond);

        OracleResult onStop = Run([M("10:30", 99.00m, 102.50m, 98.50m, 100.00m)]);
        onStop.Trade!.ExitReason.ShouldBe(OracleExitReason.Stop);
        onStop.Counters.AmbiguousMinutes.ShouldBe(0);
    }

    [Fact]
    public void X5_the_fill_minute_of_an_at_limit_entry_checks_only_the_stop()
    {
        // AtLimit entry 100.10, target 102.30: a fill minute whose high reaches the target does not exit ...
        OracleResult onlyHigh = Run([M("10:00", 100.30m, 103.00m, 100.00m, 100.20m)]);
        onlyHigh.Trade!.EntryFill.ShouldBe(OracleEntryFill.AtLimit);
        onlyHigh.Trade.ExitReason.ShouldBe(OracleExitReason.SquareOff);
        onlyHigh.Rows.ShouldContain(OracleRows.X5);

        // ... and one that also reaches the stop is a stop, not an ambiguous minute.
        OracleResult both = Run([M("10:00", 100.30m, 103.00m, 98.90m, 100.20m)]);
        both.Trade!.ExitReason.ShouldBe(OracleExitReason.Stop);
        both.Counters.AmbiguousMinutes.ShouldBe(0);
    }

    [Fact]
    public void X6_with_no_minute_at_or_after_the_square_off_the_position_leaves_at_the_last_close_as_end_of_data()
    {
        OracleResult r = Run([M("14:50", 100.50m, 100.60m, 100.40m, 100.55m)], to: "14:50");
        r.Trade!.ExitReason.ShouldBe(OracleExitReason.EndOfData);
        r.Trade.ExitPrice.ShouldBe(100.55);
        r.Rows.ShouldContain(OracleRows.X6);
        // The minutes of the next date are never read.
        Tape t = new Tape().Day(Mon, "09:15", "14:50", 100.00m).Day(Tue, "09:15", "15:29", listed: [M("09:15", 50.00m, 50.00m, 50.00m, 50.00m)]);
        OracleResult n = ReferenceWalker.Walk(t.ToArray(), Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m), Intraday(OracleSlippage.None), OracleFaults.None);
        n.Trade!.ExitReason.ShouldBe(OracleExitReason.EndOfData);
    }

    // ---- X7, X8, X9 ----
    [Fact]
    public void X7_a_swing_walk_has_no_square_off_and_no_after_square_off()
    {
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m).Day(Tue, "09:15", "15:29");
        OracleResult late = RunSwing(t, Cand(t, OracleSide.Buy, Mon, "15:15", 100.00m, 95.00m), 2);
        late.Trade.ShouldNotBeNull();
        late.Counters.AfterSquareOff.ShouldBe(0);
        late.Rows.ShouldContain(OracleRows.X7);
        OracleResult held = RunSwing(t, Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 95.00m), 5);
        held.Trade!.ExitReason.ShouldBe(OracleExitReason.EndOfData);
        held.Counters.ExitsSquareOff.ShouldBe(0);
    }

    [Fact]
    public void X8_the_due_minute_exits_at_its_close_after_the_stop_and_target_checks()
    {
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m).Day(Tue, "09:15", "15:29", listed: [M("15:29", 100.20m, 100.40m, 100.10m, 100.30m)]);
        OracleResult r = RunSwing(t, Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m), 2);
        r.Trade!.ExitReason.ShouldBe(OracleExitReason.MaxHold);
        r.Trade.ExitPrice.ShouldBe(100.30);
        r.Rows.ShouldContain(OracleRows.X8Due);

        Tape s = new Tape().Day(Mon, "09:15", "15:29", 100.00m).Day(Tue, "09:15", "15:29", listed: [M("15:29", 100.20m, 100.40m, 98.90m, 100.30m)]);
        RunSwing(s, Cand(s, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m), 2).Trade!.ExitReason.ShouldBe(OracleExitReason.Stop);
    }

    [Fact]
    public void X8_a_due_minute_without_a_trade_leaves_at_the_close_of_the_last_minute_that_holds_one()
    {
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m).Day(Tue, "09:15", "15:28", listed: [M("15:28", 100.20m, 100.25m, 100.10m, 100.15m)]).Day(Wed, "09:15", "15:29");
        OracleResult r = RunSwing(t, Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m), 2);
        r.Trade!.ExitReason.ShouldBe(OracleExitReason.MaxHold);
        r.Trade.ExitPrice.ShouldBe(100.15);
        r.Counters.MaxHoldAtLastClose.ShouldBe(1);
        r.HoldEndEpochSeconds.ShouldBe(E(Tue, "15:30"));
        r.Rows.ShouldContain(OracleRows.X8Last);
    }

    [Fact]
    public void X9_the_end_of_the_list_while_the_position_is_open_is_end_of_data_at_the_last_close()
    {
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m).Day(Tue, "09:15", "14:00", listed: [M("14:00", 100.20m, 100.30m, 100.10m, 100.25m)]);
        OracleResult r = RunSwing(t, Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m), 4);
        r.Trade!.ExitReason.ShouldBe(OracleExitReason.EndOfData);
        r.Trade.ExitPrice.ShouldBe(100.25);
        r.Rows.ShouldContain(OracleRows.X9);
    }

    // ---- S1 to S5 ----
    [Fact]
    public void S1_a_resting_limit_does_not_slip_the_at_limit_entry_and_the_target_exit()
    {
        OracleResult r = Run([M("10:00", 100.30m, 100.40m, 100.10m, 100.30m), M("10:30", 101.50m, 102.50m, 101.40m, 102.00m)], slip: Default);
        r.Trade!.EntryFilledPrice.ShouldBe(100.10);
        r.Trade.ExitReason.ShouldBe(OracleExitReason.Target);
        r.Trade.ExitFilledPrice.ShouldBe(r.Trade.ExitPrice);
    }

    [Fact]
    public void S2_a_flat_minute_slips_by_the_ticks_only_and_a_stop_exit_adds_the_extra_ticks()
    {
        // Exit at the square-off of a flat minute: a = 0.05 -> 100.00 - 0.05 = 99.95.
        Run([], slip: Default).Trade!.ExitFilledPrice.ShouldBe(99.95);
        // Stop exit at 99.00 on a flat-range minute (low 99.00 = high 99.00 impossible, so a 0.20 range): a = 0.15 + 0.01 = 0.16 -> 98.84 down 98.80.
        OracleResult stop = Run([M("10:30", 99.20m, 99.20m, 99.00m, 99.10m)], slip: Default);
        stop.Trade!.ExitFilledPrice.ShouldBe(98.80);
    }

    [Fact]
    public void S3_the_filled_prices_go_against_the_trade_on_the_grid_and_a_target_goes_on_the_grid_only()
    {
        // Entry 100.00 with stop 98.95: risk 1.05, k 2.5 -> target 102.625 (off the grid); a long target fills at tickDown = 102.60.
        OracleResult buy = Run([M("10:30", 102.60m, 102.70m, 102.50m, 102.65m)], slip: Default, stop: 98.95m, rr: 2.5);
        buy.Trade!.TargetPrice.ShouldBe(102.625);
        buy.Trade.ExitFilledPrice.ShouldBe(102.60);
        // A short: entry 100.00, stop 101.05, target 97.375 -> tickUp = 97.40.
        OracleResult sell = Run([M("10:30", 97.30m, 97.50m, 97.20m, 97.40m)], slip: Default, side: OracleSide.Sell, stop: 101.05m, rr: 2.5);
        sell.Trade!.TargetPrice.ShouldBe(97.375);
        sell.Trade.ExitFilledPrice.ShouldBe(97.40);
    }

    [Fact]
    public void S3_the_modelled_entry_never_passes_the_limit()
    {
        OracleResult r = Run([M("10:00", 100.05m, 100.30m, 99.90m, 100.20m)], slip: Default);
        r.Trade!.EntryFilledPrice.ShouldBe(100.10);
    }

    [Fact]
    public void S4_decisions_use_the_exact_prices_so_slippage_changes_no_reason_and_no_minute()
    {
        foreach (HandRow row in AllRows.Rows)
        {
            OracleResult a = ReferenceWalker.Walk(row.Bars, row.Candidate, row.Rules with { Slippage = OracleSlippage.None }, OracleFaults.None);
            OracleResult b = ReferenceWalker.Walk(row.Bars, row.Candidate, row.Rules with { Slippage = OracleSlippage.Default }, OracleFaults.None);
            a.NoTrade.ShouldBe(b.NoTrade, row.Id);
            (a.Trade is null).ShouldBe(b.Trade is null, row.Id);
            if (a.Trade is { } ta && b.Trade is { } tb)
            {
                ta.ExitReason.ShouldBe(tb.ExitReason, row.Id);
                ta.ExitEpochSeconds.ShouldBe(tb.ExitEpochSeconds, row.Id);
                ta.EntryEpochSeconds.ShouldBe(tb.EntryEpochSeconds, row.Id);
                ta.RExact.ShouldBe(tb.RExact, row.Id);
                ta.MaeR.ShouldBe(tb.MaeR, row.Id);
                ta.MfeR.ShouldBe(tb.MfeR, row.Id);
                ta.SlippageR.ShouldBe(0.0, row.Id);
                tb.SlippageR.ShouldBeLessThanOrEqualTo(0.0, row.Id);
            }
        }
    }

    [Fact]
    public void S5_a_max_hold_exit_pays_the_base_amount_with_no_extra_ticks()
    {
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m).Day(Tue, "09:15", "15:29", listed: [M("15:29", 100.60m, 100.80m, 100.50m, 100.70m)]);
        OracleResult r = RunSwing(t, Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m), 2, slip: Default);
        // a = 0.05 + 0.05 x 0.30 = 0.065: 100.635 down = 100.60 (with the extra 2 ticks it would be 100.50).
        r.Trade!.ExitFilledPrice.ShouldBe(100.60);
        r.Rows.ShouldContain(OracleRows.S5);
    }

    // ---- F13 as a whole ----
    [Fact]
    public void F13_both_sides_on_one_list_give_two_trades()
    {
        HandRow buy = AllRows.Get("F13-buy");
        HandRow sell = AllRows.Get("F13-sell");
        OracleCounters sum = ReferenceWalker.Walk(buy.Bars, buy.Candidate, buy.Rules, OracleFaults.None).Counters
            .Plus(ReferenceWalker.Walk(sell.Bars, sell.Candidate, sell.Rules, OracleFaults.None).Counters);
        sum.Trades.ShouldBe(2);
        sum.Candidates.ShouldBe(2);
    }

    // ---- The overnight shift of the H rows ----
    private static readonly string[] ShiftRows =
        ["H1", "H3", "H4", "H5", "H6", "H7", "H9", "H12", "H13", "H15", "H17", "H18", "H20", "H21", "H22", "H23", "H25"];

    [Fact]
    public void Overnight_shift_invariance_of_the_h_rows()
    {
        foreach (string id in ShiftRows)
        {
            HandRow row = AllRows.Get(id);
            int f = row.Candidate.DecisionBarIndex + 1;
            OracleBar fill = row.Bars[f];
            double close = fill.Close;
            var bars = new List<OracleBar>(row.Bars[..(f + 1)]);
            DateOnly day1 = OracleClock.DateOf(fill.EpochSeconds);
            int fillMinute = OracleClock.MinuteOfDay(fill.EpochSeconds);
            for (int m = fillMinute + 1; m <= 929; m++)
            {
                bars.Add(new OracleBar(OracleClock.Epoch(day1, m), close, close, close, close, 1000));
            }

            DateOnly day2 = day1.AddDays(1);
            for (int m = 555; m <= fillMinute; m++)
            {
                bars.Add(new OracleBar(OracleClock.Epoch(day2, m), close, close, close, close, 1000));
            }

            foreach (OracleBar b in row.Bars[(f + 1)..])
            {
                bars.Add(b with { EpochSeconds = b.EpochSeconds + 86400 });
            }

            OracleRules rules = Swing(5, null, row.Rules.Slippage, row.Rules.Tick);
            Expect x = row.Expect;
            var shifted = new Expect
            {
                Entry = x.Entry,
                EntryF = x.EntryF,
                Exit = x.Exit,
                ExitF = x.ExitF,
                Reason = x.Reason,
                R = x.R,
                RF = x.RF,
                SlipR = x.SlipR,
                Mae = x.Mae,
                Mfe = x.Mfe,
                Fill = x.Fill,
                Held = 2,
                Extra = x.Extra,
            };
            foreach (bool exact in new[] { false, true })
            {
                OracleRules r = exact ? rules with { Slippage = OracleSlippage.None } : rules;
                OracleResult res = ReferenceWalker.Walk(bars, row.Candidate, r, OracleFaults.None);
                List<string> bad = RowCheck.Compare(shifted, res, exact);
                bad.ShouldBeEmpty(id + ": " + string.Join("; ", bad));
                res.Counters.OvernightStopGaps.ShouldBe(0, id);
                res.Counters.OvernightTargetGaps.ShouldBe(0, id);
            }
        }
    }

    // ---- ADR 0047 ----
    [Fact]
    public void R1_a_double_becomes_a_decimal_by_fifteen_digits_then_eight_places_to_even()
    {
        // The stored stop is the converted stop. Entry 1.00 (trigger 1.00), tick 0.01.
        StopOf(0.123456785).Trade!.StopPrice.ShouldBe(0.12345678);
        StopOf(0.123456775).Trade!.StopPrice.ShouldBe(0.12345678);
        StopOf(0.123456786).Trade!.StopPrice.ShouldBe(0.12345679);

        // 100.000000005 -> 100.00000000 (to even), at a price level of 200.
        OracleResult hi = StopAt(200.0, 100.000000005);
        hi.Trade!.StopPrice.ShouldBe(100.0);

        // 15 significant digits: 12345678.123456785 -> 12345678.1234568 before the rounding.
        OracleResult big = StopAt(20000000.0, 12345678.123456785);
        big.Trade!.StopPrice.ShouldBe(12345678.1234568);
    }

    private static OracleResult StopOf(double stop) => StopAt(1.0, stop, 0.01m);

    private static OracleResult StopAt(double entry, double stop, decimal tick = 0.05m)
    {
        decimal e = (decimal)entry;
        Tape t = new Tape().Day(Mon, "09:15", "15:29", e);
        var c = new OracleCandidate(OracleSide.Buy, t.IndexOf(E(Mon, "10:00") - 60), E(Mon, "10:00"), entry, stop);
        return ReferenceWalker.Walk(t.ToArray(), c, Intraday(OracleSlippage.None, tick), OracleFaults.None);
    }

    [Fact]
    public void R1_a_decimal_zero_is_stored_as_plus_zero_and_a_double_input_is_not_rounded_before_conversion()
    {
        OracleResult r = Run([]);
        r.Trade!.RExact.ShouldBe(0.0);
        double.IsNegative(r.Trade.RExact).ShouldBeFalse();
        double.IsNegative(r.Trade.MaeR).ShouldBeFalse();
        double.IsNegative(r.Trade.MfeR).ShouldBeFalse();
        double.IsNegative(r.Trade.SlippageR).ShouldBeFalse();
    }

    [Fact]
    public void R2_slippage_r_is_the_difference_of_the_two_decimals_not_of_the_two_stored_doubles()
    {
        OracleTrade t = ReferenceWalker.Walk(AllRows.Get("H3").Bars, AllRows.Get("H3").Candidate, AllRows.Get("H3").Rules, OracleFaults.None).Trade!;
        t.RExact.ShouldBe(-1.4);
        t.RFilled.ShouldBe(-1.7);
        t.SlippageR.ShouldBe(-0.3);
        (t.RFilled - t.RExact).ShouldNotBe(t.SlippageR);
    }

    [Fact]
    public void R3_ties_are_touches_for_the_stop_the_target_the_open_and_the_entry()
    {
        Run([M("10:30", 99.50m, 99.60m, 99.00m, 99.40m)]).Trade!.ExitReason.ShouldBe(OracleExitReason.Stop);
        Run([M("10:30", 101.00m, 102.00m, 100.90m, 101.50m)]).Trade!.ExitReason.ShouldBe(OracleExitReason.Target);
        OracleResult open = Run([M("10:30", 102.00m, 102.10m, 101.90m, 102.00m)]);
        open.Trade!.ExitReason.ShouldBe(OracleExitReason.Target);
        open.Counters.TargetGapExits.ShouldBe(0);
        OracleResult x1 = Run([M("15:15", 99.00m, 99.10m, 98.90m, 99.00m)]);
        x1.Trade!.ExitReason.ShouldBe(OracleExitReason.Stop);
        x1.Counters.StopGapExits.ShouldBe(0);
        Run([M("10:30", 98.95m, 99.00m, 98.90m, 98.95m)]).Counters.StopGapExits.ShouldBe(1);
        // A sell mirrors: high equal to the stop touches it.
        Run([M("10:30", 100.50m, 101.00m, 100.40m, 100.60m)], side: OracleSide.Sell, stop: 101.00m).Trade!.ExitReason.ShouldBe(OracleExitReason.Stop);
    }

    [Fact]
    public void R4_the_amount_uses_the_range_of_the_minute_the_fill_happens_in()
    {
        // Entry fill minute 100.00/100.40/99.60/100.20: a = 0.05 + 0.05 x 0.80 = 0.09 -> 100.09 up = 100.10 (the limit).
        Run([M("10:00", 100.00m, 100.40m, 99.60m, 100.20m)], slip: Default).Trade!.EntryFilledPrice.ShouldBe(100.10);
        // A wider range pushes past the limit and is capped: a = 0.05 + 0.05 x 2.00 = 0.15 -> 100.15 up = 100.15, capped at 100.10.
        Run([M("10:00", 100.00m, 101.00m, 99.00m, 100.20m)], slip: Default, stop: 98.00m).Trade!.EntryFilledPrice.ShouldBe(100.10);
    }

    [Fact]
    public void R5_the_target_is_not_rounded_and_the_reward_is_exactly_k()
    {
        // k = 1.123456789 -> 1.12345679 (R1); risk 0.05 -> target 100.0561728395.
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m, [M("10:30", 100.00m, 100.10m, 99.96m, 100.06m)]);
        OracleCandidate c = Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.95m);
        OracleResult r = ReferenceWalker.Walk(t.ToArray(), c, Intraday(OracleSlippage.None, 0.01m, 1.123456789), OracleFaults.None);
        r.Trade!.TargetPrice.ShouldBe(100.0561728395);
        r.Trade.ExitReason.ShouldBe(OracleExitReason.Target);
        r.Trade.RExact.ShouldBe(1.12345679);
        r.Trade.MfeR.ShouldBe(1.12345679);
    }

    [Fact]
    public void R6_the_extremes_start_at_the_entry_and_widen_minute_by_minute()
    {
        OracleResult r = Run([M("10:30", 100.00m, 100.50m, 99.60m, 100.20m)]);
        RowCheck.R4(r.Trade!.MaeR).ShouldBe(0.4m);
        RowCheck.R4(r.Trade.MfeR).ShouldBe(0.5m);
        // A target minute that did not open beyond the target counts its adverse side in full.
        OracleResult t = Run([M("10:30", 100.00m, 102.10m, 99.50m, 102.00m)]);
        RowCheck.R4(t.Trade!.MaeR).ShouldBe(0.5m);
        RowCheck.R4(t.Trade.MfeR).ShouldBe(2.0m);
        // After a stop the favourable side of the exit minute is not counted.
        OracleResult s = Run([M("10:30", 100.00m, 101.00m, 98.50m, 99.00m)]);
        s.Trade!.MfeR.ShouldBe(0.0);
        RowCheck.R4(s.Trade.MaeR).ShouldBe(1.0m);
    }

    [Fact]
    public void R7_entries_after_missing_minutes_counts_a_fill_later_than_the_reference_instant()
    {
        Run([]).Counters.EntriesAfterMissingMinutes.ShouldBe(0);
        Run([], absent: ["10:00"]).Counters.EntriesAfterMissingMinutes.ShouldBe(1);
        Run([], decision: "15:15").Counters.EntriesAfterMissingMinutes.ShouldBe(0);
        ReferenceWalker.Walk(AllRows.Get("O1").Bars, AllRows.Get("O1").Candidate, AllRows.Get("O1").Rules, OracleFaults.None).Counters.EntriesAfterMissingMinutes.ShouldBe(0);
        ReferenceWalker.Walk(AllRows.Get("O6-0920").Bars, AllRows.Get("O6-0920").Candidate, AllRows.Get("O6-0920").Rules, OracleFaults.None).Counters.EntriesAfterMissingMinutes.ShouldBe(1);
    }

    [Fact]
    public void R8_no_target_means_no_ambiguous_minute()
    {
        Run([M("10:30", 100.50m, 102.50m, 98.50m, 100.00m)], rr: null).Counters.AmbiguousMinutes.ShouldBe(0);
    }

    [Fact]
    public void R9_zero_volume_minutes_beyond_the_five_minute_window_are_not_counted()
    {
        Mn[] zeros = [.. Enumerable.Range(0, 7).Select(i => V0($"10:{i:00}", 100.00m, 100.00m, 100.00m, 100.00m))];
        OracleResult r = Run(zeros);
        r.NoTrade.ShouldBe(OracleNoTradeReason.NoFillBar);
        r.Counters.ZeroVolumeMinutes.ShouldBe(6);
    }

    [Fact]
    public void R9_the_zero_volume_minutes_after_the_last_minute_that_holds_a_trade_at_the_end_are_not_counted()
    {
        Mn[] tail = [.. Enumerable.Range(51, 9).Select(i => V0($"14:{i:00}", 100.00m, 100.00m, 100.00m, 100.00m))];
        OracleResult r = Run([M("14:50", 100.50m, 100.60m, 100.40m, 100.55m), .. tail], to: "14:59");
        r.Trade!.ExitReason.ShouldBe(OracleExitReason.EndOfData);
        r.Counters.ZeroVolumeMinutes.ShouldBe(0);
    }

    [Fact]
    public void R10_risk_under_four_ticks_is_strict()
    {
        Run([], stop: 99.80m).Counters.RiskUnderFourTicks.ShouldBe(0);
        Run([], stop: 99.85m).Counters.RiskUnderFourTicks.ShouldBe(1);
    }

    [Fact]
    public void R13_the_hold_end_is_the_start_of_the_exit_minute()
    {
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m).Day(Tue, "09:15", "15:29", listed: [M("15:29", 100.20m, 100.40m, 100.10m, 100.30m)]);
        OracleResult r = RunSwing(t, Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m), 2);
        r.HoldEndEpochSeconds.ShouldBe(E(Tue, "15:29"));
        r.HoldEndEpochSeconds.ShouldBe(r.Trade!.ExitEpochSeconds);
    }

    [Fact]
    public void An_intraday_walk_is_never_gated_and_never_rests_overnight()
    {
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m);
        OracleResult r = ReferenceWalker.Walk(t.ToArray(), Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m), Intraday(OracleSlippage.None), OracleFaults.None);
        r.Counters.PositionOpen.ShouldBe(0);
        r.Counters.OvernightEntries.ShouldBe(0);
        r.Rows.ShouldNotContain(OracleRows.E8);
    }
}
