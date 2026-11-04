using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Options.Structures;
using XDrishti.Domain.Research;
using XDrishti.Domain.Tests.Options.Remark;

namespace XDrishti.Domain.Tests.Options.Structures;

/// <summary>
/// P14-10 criteria 1, 2 and 5 on hand series: the debit spread's maximum loss and gain at entry, units from the maximum loss, the worse leg combination of a stop inside a bar, the per-leg fallback and the unpaired states; the credit spread; the condor hand case.
/// Prices are invented round numbers in paise (a step is 5,000: 50 rupees).
/// </summary>
public sealed class StructureRemarkTests
{
    private const int Hand = 9;

    /// <summary>NIFTY step 50: the long leg at the money costs 180 and the short leg two steps up 120, until the exit bar, where they cost 140 and 110.</summary>
    private static int HandCase(int bar, int offset) => bar < Hand
        ? offset switch { 0 => 18_000, 2 => 12_000, _ => 5_000 }
        : offset switch { 0 => 14_000, 2 => 11_000, _ => 5_000 };

    [Fact]
    public void The_debit_spread_hand_case_has_a_debit_of_60_a_maximum_gain_of_40_and_16_667_units_for_a_risk_of_1000_and_a_stop_exit_at_30_loses_500_or_half_an_R()
    {
        var bars = StructureKit.Bars(RemarkKit.Tuesday, (bar, offset) => bar < Hand ? HandCase(bar, offset) : offset switch { 0 => 14_000, 2 => 11_000, _ => 5_000 });
        var trade = RemarkKit.Trade(exitHour: 10, exitMinute: 0, reason: "stop");

        var result = StructureKit.Spread(trade, bars, StructureKit.Debit());

        result.Row.Fate.ShouldBe(RemarkFates.Paired);
        (result.Row.EntryPremiumPx, result.MaxLossPx, result.MaxGainPx).ShouldBe((6_000, 6_000, 4_000));
        result.Row.Units!.Value.ShouldBe(1000.0 / 60.0, 1e-9);
        result.Row.ExitPremiumPx.ShouldBe(3_000);
        result.Row.GrossInr.ShouldBe(-500.00m);
        result.Row.REquiv!.Value.ShouldBe(-0.5, 1e-9);
        result.Row.ExitReason.ShouldBe("stop");
        result.Row.OptionSide.ShouldBe("CE");
        result.Row.StrikePx.ShouldBe(RemarkKit.BaseSpot);
        result.Row.Labels.ShouldContain("max_loss_px: 6000");
        result.Row.Labels.ShouldContain("max_gain_px: 4000");
        result.Legs.Count.ShouldBe(2);
        (result.Legs[0].Position, result.Legs[0].Side, result.Legs[0].StrikePx, result.Legs[0].EntryPremiumPx).ShouldBe(("long", "CE", RemarkKit.BaseSpot, 18_000));
        (result.Legs[1].Position, result.Legs[1].StrikePx, result.Legs[1].EntryPremiumPx, result.Legs[1].ExitPremiumPx).ShouldBe(("short", RemarkKit.BaseSpot + (2 * RemarkKit.Step), 12_000, 11_000));
    }

    [Fact]
    public void A_sell_is_a_bear_put_spread_long_the_first_strike_and_short_the_strike_below_it()
    {
        var bars = StructureKit.Bars(RemarkKit.Tuesday, (_, offset) => offset switch { 0 => 18_000, -2 => 12_000, _ => 5_000 });

        var result = StructureKit.Spread(RemarkKit.Trade(buy: false), bars, StructureKit.Debit());

        result.Row.Fate.ShouldBe(RemarkFates.Paired);
        result.Row.OptionSide.ShouldBe("PE");
        result.Legs.Select(l => (l.Position, l.StrikePx)).ShouldBe([("long", RemarkKit.BaseSpot), ("short", RemarkKit.BaseSpot - (2 * RemarkKit.Step))]);
    }

    [Fact]
    public void A_stop_inside_a_bar_takes_the_worse_leg_combination_the_short_legs_high_and_the_long_legs_low()
    {
        // entry value 5,000, stop at 50 %: level 2,500. Bar 3: open value 3,000; long low 4,000 less short high 1,700 is 2,300 (hit); the long low less the short LOW would be 2,600 (no hit).
        OptionBar? Shape(int bar, int offset, OptionBar b) => bar != 3 ? b : offset switch
        {
            0 => b with { OpenPx = 4_500, HighPx = 4_600, LowPx = 4_000, ClosePx = 4_500 },
            2 => b with { OpenPx = 1_500, HighPx = 1_700, LowPx = 1_400, ClosePx = 1_500 },
            _ => b,
        };
        var bars = StructureKit.Bars(RemarkKit.Tuesday, (_, offset) => offset switch { 0 => 6_500, 2 => 1_500, _ => 5_000 }, tweak: Shape);

        var result = StructureKit.Spread(RemarkKit.Trade(exitHour: 11), bars, StructureKit.Debit(stop: 50m));

        result.Row.Fate.ShouldBe(RemarkFates.Paired);
        result.Row.ExitReason.ShouldBe(StructureNames.StructureStop);
        result.Row.ExitTs.ShouldBe(RemarkKit.At(RemarkKit.Tuesday, 9, 30));
        result.Row.ExitPremiumPx.ShouldBe(2_500);
        result.Row.GrossInr.ShouldBe(Math.Round((2_500 - 5_000) / 100m * (decimal)result.Row.Units!.Value, 2));
    }

    [Fact]
    public void A_stop_bar_whose_legs_disagree_is_structure_inconsistent_and_never_a_price()
    {
        OptionBar? Gap(int bar, int offset, OptionBar b) => bar != 3 ? b : offset switch
        {
            0 => b with { OpenPx = 800, HighPx = 900, LowPx = 700, ClosePx = 800 },
            2 => b with { OpenPx = 1_500, HighPx = 1_600, LowPx = 1_400, ClosePx = 1_500 },
            _ => b,
        };
        var bars = StructureKit.Bars(RemarkKit.Tuesday, (_, offset) => offset switch { 0 => 6_500, 2 => 1_500, _ => 5_000 }, tweak: Gap);

        var result = StructureKit.Spread(RemarkKit.Trade(exitHour: 11), bars, StructureKit.Debit(stop: 50m));

        // long 800 less short 1,500 is below zero: a debit spread cannot be worth less than nothing, so the bars disagree; it is counted, not clamped into a full loss.
        result.Row.Fate.ShouldBe(StructureNames.StructureInconsistent);
        result.Row.ExitPremiumPx.ShouldBeNull();
        result.Row.GrossInr.ShouldBeNull();
        result.Legs.ShouldBeEmpty();
    }

    [Fact]
    public void A_short_leg_above_the_long_leg_at_the_exit_bar_is_structure_inconsistent_without_a_stop_too()
    {
        OptionBar? Bad(int bar, int offset, OptionBar b) => bar == Hand && offset == 2 ? b with { OpenPx = 20_000, HighPx = 20_050, LowPx = 19_950, ClosePx = 20_000 } : b;
        var bars = StructureKit.Bars(RemarkKit.Tuesday, (_, offset) => offset switch { 0 => 18_000, 2 => 12_000, _ => 5_000 }, tweak: Bad);

        var result = StructureKit.Spread(RemarkKit.Trade(exitHour: 10), bars, StructureKit.Debit());

        result.Row.Fate.ShouldBe(StructureNames.StructureInconsistent);
        result.Row.REquiv.ShouldBeNull();
    }

    [Fact]
    public void A_net_structure_never_loses_more_than_its_maximum_loss_plus_the_charges_and_half_spreads_of_every_fill()
    {
        // the structure goes from 60 to nothing: the full loss gross; the net loss is that plus the costs of the four fills.
        int Prem(int bar, int offset) => bar < Hand ? offset switch { 0 => 18_000, 2 => 12_000, _ => 5_000 } : offset switch { 0 => 12_000, 2 => 12_000, _ => 5_000 };
        var bars = StructureKit.Bars(RemarkKit.Tuesday, Prem);

        var result = StructureKit.Spread(RemarkKit.Trade(exitHour: 10, net: true), bars, StructureKit.Debit(), RemarkKit.Context(RemarkKit.OptionSheet, assumedPct: 1m));

        result.Row.Fate.ShouldBe(RemarkFates.Paired);
        var units = (decimal)result.Row.Units!.Value;
        var spread = new HalfSpreadValue(HalfSpreadSource.Assumed, 1m, "spread: assumed 1 % by owner", null, false);
        var day = new DateOnly(2025, 6, 10);
        var fills = result.Legs.SelectMany(l => new[]
        {
            new RemarkMoney.LegFill(l.Position == "long" ? Side.Buy : Side.Sell, l.EntryPremiumPx, day, spread),
            new RemarkMoney.LegFill(l.Position == "long" ? Side.Sell : Side.Buy, l.ExitPremiumPx, day, spread),
        }).ToList();
        var costs = -RemarkMoney.NetLegs(RemarkKit.OptionSheet, 0m, units, fills).NetInr!.Value;

        costs.ShouldBeGreaterThan(0m);
        result.Row.GrossInr!.Value.ShouldBe(-(result.MaxLossPx!.Value / 100m * units), 0.02m);
        result.Row.NetInr!.Value.ShouldBeGreaterThanOrEqualTo(-((result.MaxLossPx!.Value / 100m * units) + costs) - 0.05m);
        result.Row.NetInr.Value.ShouldBe(result.Row.GrossInr!.Value - costs, 0.05m);
    }

    [Fact]
    public void A_stop_exit_says_which_prices_its_legs_and_value_are()
    {
        OptionBar? Shape(int bar, int offset, OptionBar b) => bar != 3 ? b : offset switch
        {
            0 => b with { OpenPx = 4_500, HighPx = 4_600, LowPx = 4_000, ClosePx = 4_500 },
            2 => b with { OpenPx = 1_500, HighPx = 1_700, LowPx = 1_400, ClosePx = 1_500 },
            _ => b,
        };
        var bars = StructureKit.Bars(RemarkKit.Tuesday, (_, offset) => offset switch { 0 => 6_500, 2 => 1_500, _ => 5_000 }, tweak: Shape);

        var result = StructureKit.Spread(RemarkKit.Trade(exitHour: 11), bars, StructureKit.Debit(stop: 50m));

        result.Row.Labels.ShouldContain(StructureRemark.StopLegsLabel);
        result.Legs.Select(l => l.ExitPremiumPx).ShouldBe([4_000, 1_700]); // long at the bar low, short at the bar high
    }

    [Fact]
    public void A_stop_and_the_cash_exit_in_the_same_bar_take_the_lower_value()
    {
        // the cash exit minute 09:32 falls in the bar that starts at 09:30 and maps to the open of the next bar (09:35); the stop is reached inside the 09:30 bar by the worst combination (2,300 against a level of 2,500).
        OptionBar? Shape(int bar, int offset, OptionBar b) => bar != 3 ? b : offset switch
        {
            0 => b with { OpenPx = 5_500, HighPx = 5_600, LowPx = 4_000, ClosePx = 5_500 },
            2 => b with { OpenPx = 1_500, HighPx = 1_700, LowPx = 1_400, ClosePx = 1_500 },
            _ => b,
        };
        OptionBar? Cheaper(int bar, int offset, OptionBar b) => bar == 4 && offset == 0 ? b with { OpenPx = 2_000, HighPx = 2_100, LowPx = 1_900, ClosePx = 2_000 } : Shape(bar, offset, b);
        Func<int, int, int> prem = (_, offset) => offset switch { 0 => 6_500, 2 => 1_500, _ => 5_000 };

        var dearer = StructureKit.Spread(RemarkKit.Trade(exitHour: 9, exitMinute: 32, reason: "target"), StructureKit.Bars(RemarkKit.Tuesday, prem, tweak: Shape), StructureKit.Debit(stop: 50m));
        var lower = StructureKit.Spread(RemarkKit.Trade(exitHour: 9, exitMinute: 32, reason: "target"), StructureKit.Bars(RemarkKit.Tuesday, prem, tweak: Cheaper), StructureKit.Debit(stop: 50m));

        dearer.Row.ExitReason.ShouldBe(StructureNames.StructureStop); // the stop (2,500) is lower than the cash exit's value (5,000)
        dearer.Row.ExitPremiumPx.ShouldBe(2_500);
        lower.Row.ExitReason.ShouldBe("target"); // the cash exit's value (500) is the lower one
        lower.Row.ExitPremiumPx.ShouldBe(500);
    }

    [Fact]
    public void A_second_leg_that_cannot_be_read_at_entry_is_strike_left_window_and_a_suspect_first_leg_is_no_series()
    {
        var narrow = StructureKit.Bars(RemarkKit.Tuesday, StructureKit.Decay, window: 1);
        OptionBar? Suspect(int bar, int offset, OptionBar b) => offset == 0 ? b with { Suspect = true } : b;
        var flagged = StructureKit.Bars(RemarkKit.Tuesday, StructureKit.Decay, tweak: Suspect);

        var shortWing = StructureKit.Spread(RemarkKit.Trade(), narrow, StructureKit.Debit());
        var firstLeg = StructureKit.Spread(RemarkKit.Trade(), flagged, StructureKit.Debit());

        shortWing.Row.Fate.ShouldBe(RemarkFates.StrikeLeftWindow);
        shortWing.Legs.ShouldBeEmpty();
        firstLeg.Row.Fate.ShouldBe(RemarkFates.NoSeries);
    }

    [Fact]
    public void A_debit_at_or_above_the_width_or_at_zero_is_structure_invalid_and_a_bar_of_an_unusable_session_is_archive_unusable()
    {
        var free = StructureKit.Bars(RemarkKit.Tuesday, (_, offset) => offset == 2 ? 6_000 : 6_000); // both legs the same price: a debit of nothing
        var dear = StructureKit.Bars(RemarkKit.Tuesday, (_, offset) => offset == 0 ? 20_000 : 5_000); // 150 rupees for a 100-rupee width
        var unusable = StructureKit.Bars(RemarkKit.Tuesday, StructureKit.Decay, tweak: (_, offset, b) => offset == 2 ? b with { Usable = false } : b);

        StructureKit.Spread(RemarkKit.Trade(), free, StructureKit.Debit()).Row.Fate.ShouldBe(StructureNames.StructureInvalid);
        StructureKit.Spread(RemarkKit.Trade(), dear, StructureKit.Debit()).Row.Fate.ShouldBe(StructureNames.StructureInvalid);
        StructureKit.Spread(RemarkKit.Trade(), unusable, StructureKit.Debit()).Row.Fate.ShouldBe(RemarkFates.ArchiveUnusable);
    }

    [Fact]
    public void A_leg_that_leaves_the_window_at_the_exit_is_priced_by_the_counted_fallback_and_without_an_iv_the_structure_is_strike_left_window()
    {
        // the short strike has no bar at the exit bar (10:00): its price comes from Black-76 on its latest vendor IV; with no IV there is no honest price and the structure is not paired.
        OptionBar? Hole(int bar, int offset, OptionBar b) => bar == Hand && offset == 2 ? null : b;
        var withIv = StructureKit.Bars(RemarkKit.Tuesday, (_, offset) => offset switch { 0 => 9_000, 2 => 1_500, _ => 5_000 }, tweak: Hole);
        var noIv = StructureKit.Bars(RemarkKit.Tuesday, (_, offset) => offset switch { 0 => 9_000, 2 => 1_500, _ => 5_000 }, iv: null, tweak: Hole);

        var priced = StructureKit.Spread(RemarkKit.Trade(exitHour: 10), withIv, StructureKit.Debit());
        var nothing = StructureKit.Spread(RemarkKit.Trade(exitHour: 10), noIv, StructureKit.Debit());

        priced.Row.Fate.ShouldBe(RemarkFates.Paired);
        priced.Row.Labels.ShouldContain(RemarkFates.BsFallback);
        priced.Legs[1].Labels.ShouldContain(RemarkFates.BsFallback);
        priced.Legs[0].Labels.ShouldBeEmpty();
        nothing.Row.Fate.ShouldBe(RemarkFates.StrikeLeftWindow);
    }

    [Fact]
    public void A_credit_spread_for_a_buy_is_a_bull_put_spread_short_the_first_put_and_the_loss_is_the_width_less_the_credit()
    {
        // puts: short at the money 100, long two steps down 40: credit 60 on a 100 width, maximum loss 40, a risk of 1000 buys 25 units.
        var bars = StructureKit.Bars(RemarkKit.Tuesday, (bar, offset) => bar < Hand ? offset switch { 0 => 10_000, -2 => 4_000, _ => 3_000 } : offset switch { 0 => 7_000, -2 => 3_000, _ => 3_000 });

        var result = StructureKit.Spread(RemarkKit.Trade(exitHour: 10), bars, StructureKit.Credit());

        result.Row.Fate.ShouldBe(RemarkFates.Paired);
        result.Row.OptionSide.ShouldBe("PE");
        (result.Row.EntryPremiumPx, result.MaxLossPx, result.MaxGainPx).ShouldBe((6_000, 4_000, 6_000));
        result.Row.Units!.Value.ShouldBe(25.0, 1e-9);
        result.Row.ExitPremiumPx.ShouldBe(4_000); // cost to close: short 7,000 less long 3,000
        result.Row.GrossInr.ShouldBe(500.00m); // credit 60 less 40 to close, 25 units
        result.Legs.Select(l => (l.Position, l.Side)).ShouldBe([("short", "PE"), ("long", "PE")]);
        result.Row.Labels.ShouldContain("entry_credit_px: 6000");
    }

    [Fact]
    public void A_credit_spread_is_costed_only_on_a_measured_half_spread_an_assumption_is_spread_unavailable()
    {
        var bars = StructureKit.Bars(RemarkKit.Tuesday, (_, offset) => offset switch { 0 => 10_000, -2 => 4_000, _ => 3_000 });
        var net = RemarkKit.Trade(net: true);
        var assumed = RemarkKit.Context(RemarkKit.OptionSheet, assumedPct: 1m);
        var measured = RemarkKit.Context(RemarkKit.OptionSheet, Model(), assumedPct: 1m);

        var onAssumption = StructureKit.Spread(net, bars, StructureKit.Credit(), assumed);
        var onModel = StructureKit.Spread(net, bars, StructureKit.Credit(), measured);
        var debitOnAssumption = StructureKit.Spread(net, StructureKit.Bars(RemarkKit.Tuesday, StructureKit.Decay), StructureKit.Debit(), assumed);

        onAssumption.Row.Fate.ShouldBe(RemarkFates.SpreadUnavailable);
        onAssumption.Row.Labels.ShouldContain(StructureNames.SpreadNotMeasuredLabel);
        onModel.Row.Fate.ShouldBe(RemarkFates.Paired);
        onModel.Row.NetInr.ShouldNotBeNull();
        onModel.Row.NetInr!.Value.ShouldBeLessThan(onModel.Row.GrossInr!.Value);
        debitOnAssumption.Row.Fate.ShouldBe(RemarkFates.Paired); // a debit spread runs on the owner's assumption, labelled
        debitOnAssumption.Row.Labels.ShouldContain("spread: assumed 1 % by owner");
    }

    [Fact]
    public void The_condor_hand_case_has_a_credit_of_40_on_100_point_wings_a_maximum_loss_of_60_and_four_legs_with_no_cash_twin()
    {
        // 50 on each short leg and 30 on each long wing: credit 40 for two sides, wings 100 points (two steps of 50).
        int Prem(int bar, int offset) => Math.Abs(offset) switch { 6 => 5_000, 8 => 3_000, _ => 9_000 };
        var calls = StructureKit.TwoDays(Prem);
        var puts = StructureKit.TwoDays(Prem);

        var result = StructureRemark.EvaluateCondor(StructureKit.CondorTrade(), RemarkKit.Plan, CondorParams.Primary, BarGrid.Build(calls), BarGrid.Build(puts), RemarkKit.Context());

        result.Row.Fate.ShouldBe(RemarkFates.Paired);
        (result.Row.EntryPremiumPx, result.MaxLossPx, result.MaxGainPx).ShouldBe((4_000, 6_000, 4_000));
        result.Row.OptionSide.ShouldBe("IC");
        result.Row.Units.ShouldBe(75.0); // one lot
        result.Row.CashR.ShouldBeNull();
        result.Row.Labels.ShouldContain(StructureNames.NoCashTwin);
        result.Row.ExitReason.ShouldBe(RemarkFates.ExpiryExit);
        result.Row.ExitTs.ShouldBe(RemarkKit.At(StructureKit.Wednesday, 15, 30));
        result.Row.GrossInr.ShouldBe(0m); // nothing moved: closed at the entry value
        result.Legs.Select(l => (l.Leg, l.Side, l.Position)).ShouldBe([(0, "CE", "short"), (1, "CE", "long"), (2, "PE", "short"), (3, "PE", "long")]);
        result.Legs.Select(l => l.StrikePx).ShouldBe([RemarkKit.BaseSpot + (6 * RemarkKit.Step), RemarkKit.BaseSpot + (8 * RemarkKit.Step), RemarkKit.BaseSpot - (6 * RemarkKit.Step), RemarkKit.BaseSpot - (8 * RemarkKit.Step)]);
    }

    [Fact]
    public void The_condor_stop_is_a_loss_of_m_times_the_credit_and_its_loss_is_capped_at_the_maximum_loss()
    {
        // from bar 5 on Tuesday the call side is worth 90 and 30 (a loss of 60 against a credit of 40): the stop at 1 x the credit is a close value of 80 (8,000 paise), reached at the next bar.
        int Calls(int bar, int offset) => Math.Abs(offset) switch { 6 => bar >= 5 ? 9_000 : 5_000, 8 => 3_000, _ => 9_000 };
        int Puts(int bar, int offset) => Math.Abs(offset) switch { 6 => 5_000, 8 => 3_000, _ => 9_000 };

        var result = StructureRemark.EvaluateCondor(StructureKit.CondorTrade(), RemarkKit.Plan, CondorParams.Primary, BarGrid.Build(StructureKit.TwoDays(Calls)), BarGrid.Build(StructureKit.TwoDays(Puts)), RemarkKit.Context());

        result.Row.ExitReason.ShouldBe(StructureNames.CondorStop);
        result.Row.ExitPremiumPx!.Value.ShouldBeGreaterThanOrEqualTo(8_000);
        result.Row.ExitPremiumPx.Value.ShouldBeLessThanOrEqualTo(10_000);
        result.Row.GrossInr!.Value.ShouldBeGreaterThanOrEqualTo(-result.MaxLossPx!.Value / 100m * 75m);
    }

    [Fact]
    public void A_condor_with_an_unknown_lot_is_lot_unknown_and_never_sized_by_a_guess()
    {
        int Prem(int bar, int offset) => Math.Abs(offset) switch { 6 => 5_000, 8 => 3_000, _ => 9_000 };

        var result = StructureRemark.EvaluateCondor(StructureKit.CondorTrade(), RemarkKit.Plan, CondorParams.Primary, BarGrid.Build(StructureKit.TwoDays(Prem)), BarGrid.Build(StructureKit.TwoDays(Prem)), RemarkKit.Context(lots: []));

        result.Row.Fate.ShouldBe(StructureNames.LotUnknown);
    }

    [Fact]
    public void The_same_inputs_give_the_same_row_and_a_row_never_exits_before_it_enters()
    {
        var bars = StructureKit.Bars(RemarkKit.Tuesday, HandCase);

        var first = StructureKit.Spread(RemarkKit.Trade(), bars, StructureKit.Debit());
        var second = StructureKit.Spread(RemarkKit.Trade(), bars, StructureKit.Debit());

        first.Row.Labels.ShouldBe(second.Row.Labels);
        (first.Row.EntryTs, first.Row.ExitTs, first.Row.GrossInr, first.Row.REquiv, first.Row.Units).ShouldBe((second.Row.EntryTs, second.Row.ExitTs, second.Row.GrossInr, second.Row.REquiv, second.Row.Units));
        first.Row.ExitTs.ShouldBeGreaterThanOrEqualTo(first.Row.EntryTs);
    }

    private static HalfSpreadModel Model() => new("m1", RemarkKit.Underlying, 60, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 1), [new SpreadBucketRow(SpreadBuckets.All, "0-2", SpreadBuckets.All, 100, 0.5), new SpreadBucketRow(SpreadBuckets.All, "3-7", SpreadBuckets.All, 100, 0.5)]);
}
