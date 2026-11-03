using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Pricing;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Tests.Options.Remark;

/// <summary>P14-08 criteria 1 to 3 and 7 on hand series (synthetic bars, invented prices): the entry and exit bars, the held strike, the worse outcome, the expiry exit and every honest fate.</summary>
public sealed class OptionRemarkTests
{
    private static RemarkRow Pair(RemarkTrade? trade = null, Func<int, int>? spot = null, OptionProfileParams? profile = null, RemarkContext? context = null, bool call = true, Func<OptionBar, OptionBar>? tweak = null, int window = 10, SeriesPlan? plan = null)
    {
        var t = trade ?? RemarkKit.Trade();
        return RemarkKit.Evaluate(t, RemarkKit.Day(t.TradeDate, call, spot ?? RemarkKit.Flat, window: window, tweak: tweak), profile, context, plan);
    }

    [Fact]
    public void The_entry_is_the_open_of_the_first_option_bar_at_or_after_the_cash_minute_and_the_strike_is_the_money_strike_of_that_bar()
    {
        var row = Pair(RemarkKit.Trade(entryMinute: 17));

        row.Fate.ShouldBe(RemarkFates.Paired);
        row.EntryTs.ShouldBe(RemarkKit.At(RemarkKit.Tuesday, 9, 20));
        row.StrikePx.ShouldBe(RemarkKit.BaseSpot);
        row.EntryPremiumPx.ShouldBe(6000);
        row.OptionSide.ShouldBe("CE");
        row.Expiry.ShouldBe(RemarkKit.Thursday);
        row.ExpirySource.ShouldBe("derived");
        row.Labels.ShouldContain(RemarkFates.ExpiryDerived);
        row.Labels.ShouldContain(RemarkFates.BarTimestampLabel);
    }

    [Fact]
    public void A_cash_minute_exactly_on_a_bar_start_enters_that_bar_and_the_option_never_fills_earlier_than_the_cash_fill()
    {
        var row = Pair(RemarkKit.Trade(entryMinute: 20));

        row.EntryTs.ShouldBe(RemarkKit.At(RemarkKit.Tuesday, 9, 20));
        row.EntryTs.ShouldBeGreaterThanOrEqualTo(RemarkKit.At(RemarkKit.Tuesday, 9, 20));
    }

    [Fact]
    public void A_put_is_bought_for_a_sell_and_an_in_the_money_strike_is_one_step_inside()
    {
        var call = Pair(profile: OptionProfileParams.Create("itm1", "nearest_safe", null, 1).Value);
        var put = Pair(RemarkKit.Trade(buy: false), call: false, profile: OptionProfileParams.Create("itm1", "nearest_safe", null, 1).Value);

        call.OptionSide.ShouldBe("CE");
        call.StrikePx.ShouldBe(RemarkKit.BaseSpot - RemarkKit.Step);
        put.OptionSide.ShouldBe("PE");
        put.StrikePx.ShouldBe(RemarkKit.BaseSpot + RemarkKit.Step);
    }

    [Fact]
    public void The_cash_exit_maps_to_the_open_of_the_first_bar_at_or_after_the_exit_minute()
    {
        var row = Pair(RemarkKit.Trade(exitHour: 10, exitMinute: 2), spot: bar => RemarkKit.BaseSpot + (bar * 100));

        row.ExitTs.ShouldBe(RemarkKit.At(RemarkKit.Tuesday, 10, 5));
        row.ExitPremiumPx.ShouldBe(RemarkKit.Prem(true, RemarkKit.BaseSpot + (10 * 100), RemarkKit.BaseSpot));
        row.ExitReason.ShouldBe("target");
    }

    [Fact]
    public void A_session_square_off_leaves_at_the_close_of_the_last_bar_of_the_session()
    {
        var row = Pair(RemarkKit.Trade(exitHour: 15, exitMinute: 29, reason: "square_off"), spot: bar => RemarkKit.BaseSpot + (bar * 10));

        row.ExitTs.ShouldBe(RemarkKit.At(RemarkKit.Tuesday, 15, 30));
        row.ExitPremiumPx.ShouldBe(RemarkKit.Prem(true, RemarkKit.BaseSpot + (74 * 10), RemarkKit.BaseSpot));
        row.ExitReason.ShouldBe("square_off");
    }

    [Fact]
    public void The_units_carry_the_cash_rupee_risk_over_the_delta_of_the_entry_bar_on_its_vendor_iv()
    {
        var row = Pair();
        var years = ExpiryTime.YearFraction(RemarkKit.At(RemarkKit.Tuesday, 9, 20), RemarkKit.Thursday).Value;
        var delta = Black76.Greeks(OptionKind.Call, RemarkKit.BaseSpot / 100.0, RemarkKit.BaseSpot / 100.0, years, 1.0, 0.15).Value.Delta;

        row.Units.ShouldNotBeNull();

        // risk 100 x 10 = 1,000; units = 1,000 / (|delta| x 10) = 100 / |delta|
        row.Units.Value.ShouldBe(100.0 / Math.Abs(delta), 1e-9);
        row.Labels.ShouldContain(RemarkFates.DeltaLabel);
        row.Lot.ShouldBe(75);

        // one lot loses 75 x |delta| x 10 = about 375 against a risk of 1,000
        row.Sizable.ShouldBe("sizable");
    }

    [Fact]
    public void The_gross_r_equiv_is_the_gross_rupees_over_the_cash_risk_and_the_cash_r_is_carried()
    {
        var row = Pair(spot: bar => RemarkKit.BaseSpot + (bar * 200));

        row.GrossInr.ShouldNotBeNull();
        row.NetInr.ShouldBeNull();
        row.REquiv!.Value.ShouldBe((double)(row.GrossInr.Value / 1000m), 1e-12);
        row.CashR!.Value.ShouldBe(2.0);
        row.Labels.ShouldContain("basis: gross");
    }

    [Fact]
    public void While_the_spot_rises_three_strike_steps_the_held_strike_is_read_from_the_offsets_0_minus_1_minus_2_and_minus_3_in_turn()
    {
        // the spot climbs one step every 10 bars (50 minutes for 5-minute bars): the held strike is the entry strike all the time
        var bars = RemarkKit.Day(RemarkKit.Tuesday, true, bar => RemarkKit.BaseSpot + (bar / 10 * RemarkKit.Step));
        var series = new HeldStrikeSeries(BarGrid.Build(bars), RemarkKit.BaseSpot);
        var offsets = new List<int>();
        for (var bar = 0; bar < 40; bar += 10)
        {
            series.Held(RemarkKit.At(RemarkKit.Tuesday, 9, 15).AddMinutes(bar * 5), out var held).ShouldBe(HeldState.Available);
            offsets.Add(held!.Offset);
        }

        offsets.ShouldBe([0, -1, -2, -3]);
    }

    [Fact]
    public void A_strike_that_left_the_window_is_priced_by_the_model_on_an_iv_no_older_than_the_setting_and_labelled()
    {
        // at bar 30 (11:45) the spot jumps five steps: the entry strike is outside +-3 from then on; exit at 12:35 (bar 40), the last vendor bar of the strike is 11:40, 55 minutes before
        var trade = RemarkKit.Trade(exitHour: 12, exitMinute: 35);
        Func<int, int> jump = bar => bar >= 30 ? RemarkKit.BaseSpot + (5 * RemarkKit.Step) : RemarkKit.BaseSpot;

        var modelled = Pair(trade, jump, window: 3, context: RemarkKit.Context(maxIvAge: 60));
        var refused = Pair(trade, jump, window: 3, context: RemarkKit.Context(maxIvAge: 30));
        var noSpot = Pair(trade, jump, window: 3, context: RemarkKit.Context(spot: _ => null));

        modelled.Fate.ShouldBe(RemarkFates.Paired);
        modelled.Labels.ShouldContain(RemarkFates.BsFallback);
        modelled.ExitPremiumPx.ShouldNotBeNull();
        modelled.ExitPremiumPx.Value.ShouldBeGreaterThan(0);
        refused.Fate.ShouldBe(RemarkFates.StrikeLeftWindow);
        noSpot.Fate.ShouldBe(RemarkFates.StrikeLeftWindow);
        refused.EntryPremiumPx.ShouldBeNull();
    }

    [Fact]
    public void A_premium_stop_exits_at_the_level_when_the_bar_low_reaches_it()
    {
        // entry premium 6,000 paise: a 30 % stop is 4,200; the 09:40 bar trades down to 4,000
        var row = Pair(profile: RemarkKit.WithStop(30m), tweak: bar => bar.Ts == RemarkKit.At(RemarkKit.Tuesday, 9, 40) ? bar with { LowPx = 4000 } : bar);

        row.ExitReason.ShouldBe(RemarkFates.PremiumStop);
        row.ExitPremiumPx.ShouldBe(4200);
        row.ExitTs.ShouldBe(RemarkKit.At(RemarkKit.Tuesday, 9, 40));
    }

    [Fact]
    public void A_bar_that_opens_below_the_premium_stop_exits_at_its_open()
    {
        var row = Pair(profile: RemarkKit.WithStop(30m), tweak: bar => bar.Ts == RemarkKit.At(RemarkKit.Tuesday, 9, 40) ? bar with { OpenPx = 3900, LowPx = 3800 } : bar);

        row.ExitReason.ShouldBe(RemarkFates.PremiumStop);
        row.ExitPremiumPx.ShouldBe(3900);
    }

    [Theory]
    [InlineData(5500, "premium_stop", 4200)]
    [InlineData(3000, "target", 3000)]
    public void When_the_premium_stop_and_the_cash_exit_fall_in_the_same_option_bar_the_lower_premium_is_taken(int cashBarOpen, string reason, int expected)
    {
        // the cash exit is at 09:42, inside the 09:40 bar; the next bar (09:45) is where the cash exit fills. The 09:40 bar reaches the stop level 4,200 (30 % under the 6,000 entry).
        var trade = RemarkKit.Trade(exitHour: 9, exitMinute: 42);
        Func<OptionBar, OptionBar> tweak = bar => bar.Ts == RemarkKit.At(RemarkKit.Tuesday, 9, 40) ? bar with { LowPx = 4100 } : bar.Ts == RemarkKit.At(RemarkKit.Tuesday, 9, 45) ? bar with { OpenPx = cashBarOpen, LowPx = Math.Min(cashBarOpen, bar.LowPx) } : bar;

        var row = Pair(trade, profile: RemarkKit.WithStop(30m), tweak: tweak);

        row.ExitReason.ShouldBe(reason);
        row.ExitPremiumPx.ShouldBe(expected);
    }

    [Fact]
    public void A_premium_stop_in_an_earlier_bar_than_the_one_holding_the_cash_exit_is_simply_the_stop()
    {
        var trade = RemarkKit.Trade(exitHour: 10, exitMinute: 0);

        var row = Pair(trade, profile: RemarkKit.WithStop(30m), tweak: bar => bar.Ts == RemarkKit.At(RemarkKit.Tuesday, 9, 30) ? bar with { LowPx = 4000 } : bar);

        row.ExitReason.ShouldBe(RemarkFates.PremiumStop);
        row.ExitTs.ShouldBe(RemarkKit.At(RemarkKit.Tuesday, 9, 30));
    }

    [Fact]
    public void A_swing_trade_open_at_the_close_of_the_last_session_before_the_expiry_leaves_at_that_close()
    {
        var day2 = new DateOnly(2025, 6, 11);
        var trade = RemarkKit.Trade(exitHour: 10, exitMinute: 0, reason: "max_hold") with { ExitEpoch = RemarkKit.Epoch(new DateOnly(2025, 6, 12), 10, 0) };
        var bars = RemarkKit.Day(RemarkKit.Tuesday, true, RemarkKit.Flat).Concat(RemarkKit.Day(day2, true, RemarkKit.Flat)).ToList();

        var row = RemarkKit.Evaluate(trade, bars);

        row.Fate.ShouldBe(RemarkFates.Paired);
        row.ExitReason.ShouldBe(RemarkFates.ExpiryExit);
        row.ExitTs.ShouldBe(RemarkKit.At(day2, 15, 30));
    }

    [Fact]
    public void An_expiry_whose_last_session_has_no_bars_and_no_recent_iv_is_strike_left_window_never_no_series()
    {
        var trade = RemarkKit.Trade() with { ExitEpoch = RemarkKit.Epoch(new DateOnly(2025, 6, 12), 10, 0) };
        var bars = RemarkKit.Day(RemarkKit.Tuesday, true, RemarkKit.Flat);

        var refused = RemarkKit.Evaluate(trade, bars, context: RemarkKit.Context(maxIvAge: 390));

        refused.Fate.ShouldBe(RemarkFates.StrikeLeftWindow);
        AssertNoFigures(refused);
    }

    [Fact]
    public void A_truncated_last_session_does_not_exit_before_its_close()
    {
        var day2 = new DateOnly(2025, 6, 11);
        var trade = RemarkKit.Trade() with { ExitEpoch = RemarkKit.Epoch(new DateOnly(2025, 6, 12), 10, 0) };
        var bars = RemarkKit.Day(RemarkKit.Tuesday, true, RemarkKit.Flat).Concat(RemarkKit.Day(day2, true, RemarkKit.Flat).Where(b => b.Ts < RemarkKit.At(day2, 12, 0))).ToList();

        var row = RemarkKit.Evaluate(trade, bars, context: RemarkKit.Context(maxIvAge: 390));

        row.ExitTs.ShouldBe(RemarkKit.At(day2, 15, 30));
        RemarkKit.Evaluate(trade, bars).Fate.ShouldBe(RemarkFates.StrikeLeftWindow);
    }

    [Fact]
    public void A_hole_in_the_bars_at_the_cash_exit_is_priced_at_the_cash_exit_instant_not_moved_to_another_bar()
    {
        var trade = RemarkKit.Trade(exitHour: 10, exitMinute: 12);
        Func<OptionBar, bool> hole = b => b.Ts >= RemarkKit.At(RemarkKit.Tuesday, 10, 0) && b.Ts < RemarkKit.At(RemarkKit.Tuesday, 10, 30);

        var row = RemarkKit.Evaluate(trade, RemarkKit.Day(RemarkKit.Tuesday, true, RemarkKit.Flat).Where(b => !hole(b)));
        var stale = RemarkKit.Evaluate(trade, RemarkKit.Day(RemarkKit.Tuesday, true, RemarkKit.Flat).Where(b => !hole(b)), context: RemarkKit.Context(maxIvAge: 10));

        row.Fate.ShouldBe(RemarkFates.Paired);
        row.ExitTs.ShouldBe(RemarkKit.At(RemarkKit.Tuesday, 10, 12));
        row.Labels.ShouldContain(RemarkFates.BsFallback);
        stale.Fate.ShouldBe(RemarkFates.StrikeLeftWindow);
    }

    [Fact]
    public void A_session_truncated_before_the_cash_exit_is_priced_at_the_exit_instant_and_the_close_is_used_only_from_the_last_regular_bar()
    {
        var bars = RemarkKit.Day(RemarkKit.Tuesday, true, RemarkKit.Flat).Where(b => b.Ts < RemarkKit.At(RemarkKit.Tuesday, 12, 0)).ToList();

        var midday = RemarkKit.Evaluate(RemarkKit.Trade(exitHour: 12, exitMinute: 30), bars);
        var late = RemarkKit.Evaluate(RemarkKit.Trade(exitHour: 15, exitMinute: 27, reason: "square_off"), bars, context: RemarkKit.Context(maxIvAge: 390));

        midday.Fate.ShouldBe(RemarkFates.Paired);
        midday.ExitTs.ShouldBe(RemarkKit.At(RemarkKit.Tuesday, 12, 30));
        late.ExitTs.ShouldBe(RemarkKit.At(RemarkKit.Tuesday, 15, 30));
    }

    [Fact]
    public void A_bar_of_an_unusable_key_session_between_entry_and_exit_is_archive_unusable_without_a_premium_stop_too_and_the_rows_say_the_planned_hold_and_the_iv_unit()
    {
        var trade = RemarkKit.Trade(exitHour: 10, exitMinute: 0);

        var unusable = Pair(trade, tweak: bar => bar.Ts == RemarkKit.At(RemarkKit.Tuesday, 9, 40) ? bar with { Usable = false } : bar);
        var ok = Pair(trade);

        unusable.Fate.ShouldBe(RemarkFates.ArchiveUnusable);
        ok.Labels.ShouldContain("planned_hold_sessions: 1 (1 = intraday)");
        ok.Labels.ShouldContain("iv_unit: percent_assumed");
    }

    [Fact]
    public void No_bars_at_all_is_no_series()
    {
        RemarkKit.Evaluate(RemarkKit.Trade(), []).Fate.ShouldBe(RemarkFates.NoSeries);
    }

    [Fact]
    public void An_entry_bar_from_an_unusable_key_session_is_archive_unusable()
    {
        Pair(tweak: bar => bar with { Usable = false }).Fate.ShouldBe(RemarkFates.ArchiveUnusable);
    }

    [Fact]
    public void A_held_bar_that_is_unusable_on_the_way_is_archive_unusable_and_a_suspect_entry_bar_is_unavailable()
    {
        Pair(RemarkKit.Trade(exitHour: 10, exitMinute: 0), tweak: bar => bar.Ts == RemarkKit.At(RemarkKit.Tuesday, 10, 0) ? bar with { Usable = false } : bar).Fate.ShouldBe(RemarkFates.ArchiveUnusable);
        Pair(tweak: bar => bar with { Suspect = true }).Fate.ShouldBe(RemarkFates.NoSeries);
    }

    [Fact]
    public void An_entry_bar_without_an_iv_is_no_iv_and_carries_no_figure()
    {
        var row = Pair(tweak: bar => bar with { Iv = null });

        row.Fate.ShouldBe(RemarkFates.NoIv);
        AssertNoFigures(row);
    }

    [Fact]
    public void An_open_interest_below_the_threshold_is_illiquid_and_a_missing_open_interest_is_liquidity_unknown_never_a_pass()
    {
        Pair(tweak: bar => bar with { Oi = 10 }).Fate.ShouldBe(RemarkFates.Illiquid);
        Pair(tweak: bar => bar with { Oi = null }).Fate.ShouldBe(RemarkFates.LiquidityUnknown);
        Pair(tweak: bar => bar with { Volume = null }).Fate.ShouldBe(RemarkFates.LiquidityUnknown);
    }

    [Fact]
    public void A_cash_trade_net_of_charges_against_an_option_side_that_is_gross_is_basis_mismatch_and_so_is_a_changed_sheet()
    {
        Pair(RemarkKit.Trade(net: true)).Fate.ShouldBe(RemarkFates.BasisMismatch);
        Pair(RemarkKit.Trade(net: false), context: RemarkKit.Context(RemarkKit.OptionSheet, assumedPct: 1m)).Fate.ShouldBe(RemarkFates.BasisMismatch);
        Pair(RemarkKit.Trade(net: true), context: RemarkKit.Context(RemarkKit.OptionSheet, assumedPct: 1m, sheetChanged: true)).Fate.ShouldBe(RemarkFates.BasisMismatch);
    }

    [Fact]
    public void A_net_trade_whose_sheet_has_no_option_lines_is_basis_mismatch_not_a_zero_charge()
    {
        var row = Pair(RemarkKit.Trade(net: true), context: RemarkKit.Context(XDrishti.Tests.Shared.TestCostSheet.Sheet, assumedPct: 1m));

        row.Fate.ShouldBe(RemarkFates.BasisMismatch);
        row.Labels.ShouldContain(l => l.StartsWith("costs: none", StringComparison.Ordinal));
    }

    [Fact]
    public void A_net_trade_with_no_spread_model_and_no_assumption_is_spread_unavailable()
    {
        var row = Pair(RemarkKit.Trade(net: true), context: RemarkKit.Context(RemarkKit.OptionSheet));

        row.Fate.ShouldBe(RemarkFates.SpreadUnavailable);
        AssertNoFigures(row);
    }

    [Fact]
    public void A_trade_still_open_at_the_end_of_the_cash_run_is_open_at_end_and_a_stop_at_the_entry_has_no_risk()
    {
        Pair(RemarkKit.Trade(open: true)).Fate.ShouldBe(RemarkFates.OpenAtEnd);
        Pair(RemarkKit.Trade(stop: 1000m)).Fate.ShouldBe(RemarkFates.RiskInvalid);
    }

    [Fact]
    public void Every_unpaired_row_carries_no_premium_no_units_no_rupees_and_no_r()
    {
        var rows = new[]
        {
            Pair(tweak: bar => bar with { Iv = null }),
            Pair(tweak: bar => bar with { Oi = 10 }),
            Pair(RemarkKit.Trade(open: true)),
            Pair(RemarkKit.Trade(net: true)),
            Pair(tweak: bar => bar with { Usable = false }),
            RemarkKit.Evaluate(RemarkKit.Trade(), []),
        };

        rows.Select(r => r.Fate).ShouldBe([RemarkFates.NoIv, RemarkFates.Illiquid, RemarkFates.OpenAtEnd, RemarkFates.BasisMismatch, RemarkFates.ArchiveUnusable, RemarkFates.NoSeries]);
        foreach (var row in rows)
        {
            AssertNoFigures(row);
        }
    }

    [Fact]
    public void Every_unpaired_reason_of_the_closed_list_is_reached_by_a_fixture()
    {
        var calendar = new XDrishti.Domain.Market.Calendar.TradingCalendar();
        var nifty = new ExpiryCalendar(ExpiryRules.Nifty, [], [], calendar);
        var share = new ExpiryCalendar("NSE:RELIANCE", [], [], calendar);
        var none = new ExpiryCalendar("NSE:X", [], [], new XDrishti.Domain.Market.Calendar.TradingCalendar([.. Enumerable.Range(0, 500).Select(i => XDrishti.Domain.Market.Calendar.TradingDay.Holiday(new DateOnly(2025, 6, 1).AddDays(i), XDrishti.Domain.Market.Calendar.CalendarSource.Manual, XDrishti.Domain.Market.Calendar.CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch))]));
        var reached = new HashSet<string>
        {
            SeriesPlanner.Plan("NSE:RELIANCE", new DateOnly(2025, 6, 25), new DateOnly(2025, 6, 25), share, []).Fate!,
            SeriesPlanner.Plan("NSE:X", RemarkKit.Tuesday, RemarkKit.Tuesday, none, []).Fate!,
            RemarkKit.Evaluate(RemarkKit.Trade(), []).Fate,
            Pair(window: 3, trade: RemarkKit.Trade(exitHour: 12, exitMinute: 35), spot: bar => bar >= 30 ? RemarkKit.BaseSpot + (5 * RemarkKit.Step) : RemarkKit.BaseSpot, context: RemarkKit.Context(maxIvAge: 30)).Fate,
            Pair(tweak: bar => bar with { Usable = false }).Fate,
            Pair(tweak: bar => bar with { Iv = null }).Fate,
            Pair(tweak: bar => bar with { Oi = 10 }).Fate,
            Pair(tweak: bar => bar with { Oi = null }).Fate,
            Pair(RemarkKit.Trade(net: true)).Fate,
            Pair(RemarkKit.Trade(open: true)).Fate,
            Pair(RemarkKit.Trade(net: true), context: RemarkKit.Context(RemarkKit.OptionSheet)).Fate,
            Pair(RemarkKit.Trade(stop: 1000m)).Fate,
        };

        nifty.Nth('W', RemarkKit.Tuesday, 1).ShouldNotBeNull("the planner fixtures are not vacuous");
        reached.OrderBy(f => f, StringComparer.Ordinal).ShouldBe(RemarkFates.Unpaired.OrderBy(f => f, StringComparer.Ordinal));
    }

    [Fact]
    public void The_same_trade_on_two_dates_records_the_lot_in_force_on_each()
    {
        var lots = new[]
        {
            new LotSizeEntry(1, RemarkKit.Underlying, new DateOnly(2024, 11, 20), 75, LotSizeEntry.Secondary, false),
            new LotSizeEntry(2, RemarkKit.Underlying, new DateOnly(2025, 12, 30), 65, LotSizeEntry.Secondary, false),
        };
        SeriesPlan PlanFor(DateOnly day) => new('W', 1, new ExpiryDate(day.AddDays(7), false), 5, day.AddDays(6));

        var before = new DateOnly(2025, 12, 29);
        var after = new DateOnly(2025, 12, 30);
        var a = Pair(RemarkKit.Trade(day: before), context: RemarkKit.Context(lots: lots), plan: PlanFor(before));
        var b = Pair(RemarkKit.Trade(day: after), context: RemarkKit.Context(lots: lots), plan: PlanFor(after));

        a.Lot.ShouldBe(75);
        b.Lot.ShouldBe(65);
        a.Sizable.ShouldBe("sizable");
        b.Sizable.ShouldBe("sizable");
        var small = Pair(RemarkKit.Trade(day: after, quantity: 10m), context: RemarkKit.Context(lots: lots), plan: PlanFor(after));
        small.Sizable.ShouldBe("not_sizable");
        RemarkKit.Evaluate(RemarkKit.Trade(day: before), RemarkKit.Day(before, true, RemarkKit.Flat), context: RemarkKit.Context(lots: []), plan: PlanFor(before)).Sizable.ShouldBe("lot_unknown");
    }

    [Fact]
    public void A_net_trade_is_costed_with_the_charges_of_each_leg_and_the_assumed_half_spread_and_labelled()
    {
        var row = Pair(RemarkKit.Trade(net: true), context: RemarkKit.Context(RemarkKit.OptionSheet, assumedPct: 1m), spot: bar => RemarkRunKit.Up(bar));

        row.Fate.ShouldBe(RemarkFates.Paired);
        row.NetInr.ShouldNotBeNull();
        row.NetInr.Value.ShouldBeLessThan(row.GrossInr!.Value);
        row.REquiv!.Value.ShouldBe((double)(row.NetInr.Value / 1000m), 1e-12);
        row.Labels.ShouldContain("basis: net");
        row.Labels.ShouldContain("spread: assumed 1 % by owner");
        row.Labels.ShouldContain(XDrishti.Domain.Research.CostDescription.SheetLabel(RemarkKit.OptionSheet));
    }

    private static void AssertNoFigures(RemarkRow row)
    {
        row.IsPaired.ShouldBeFalse();
        row.EntryPremiumPx.ShouldBeNull();
        row.ExitPremiumPx.ShouldBeNull();
        row.Units.ShouldBeNull();
        row.GrossInr.ShouldBeNull();
        row.NetInr.ShouldBeNull();
        row.REquiv.ShouldBeNull();
        row.CashR.ShouldBeNull();
        row.StrikePx.ShouldBeNull();
        row.Sizable.ShouldBe(RemarkRow.NotApplicable);
    }
}

internal static class RemarkRunKit
{
    public static int Up(int bar) => RemarkKit.BaseSpot + (bar * 100);
}
