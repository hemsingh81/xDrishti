using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// Criteria 11, 13, 18 and the edge cases of P3-03c: the worked trades through <see cref="TradeCostApplier"/> against the hand figures (quantity, risk taken, both turnovers, both legs' eight lines, total, charge in R, net R), the skips and their
/// order, the boundary of <c>max_cost_R</c>, a SELL charged on its own sides, a swing-shaped trade, and the failure when the sheet has no rates for a date.
/// </summary>
public sealed class TradeCostApplierTests
{
    private static readonly DateOnly Mar2 = new(2026, 3, 2);

    private static readonly DateOnly Mar3 = new(2026, 3, 3);

    private static SimTrade Trade(string name) => name switch
    {
        "W1" => CostTrades.W1(),
        "W2" => CostTrades.Make(Side.Sell, Mar2, 199.80, 202.00, 195.40),
        "W3" => CostTrades.Make(Side.Buy, Mar2, 100.00, 99.80, 100.40, entryFilled: 100.10),
        "W5" => CostTrades.W1(Mar3),
        "W6" => CostTrades.W1(),
        "W7A" => CostTrades.Make(Side.Buy, Mar2, 100.50, 98.00, 105.50),
        "W7B" => CostTrades.Make(Side.Buy, Mar3, 100.50, 98.00, 105.50),
        "W8" => CostTrades.Make(Side.Buy, Mar2, 100.50, 98.00, 98.00, reason: ExitReason.Stop),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static WorkedCosts.HandTrade Hand(string name) => name switch
    {
        "W1" => WorkedCosts.W1,
        "W2" => WorkedCosts.W2,
        "W5" => WorkedCosts.W5,
        "W6" => WorkedCosts.W6,
        "W7A" => WorkedCosts.W7A,
        "W7B" => WorkedCosts.W7B,
        "W8" => WorkedCosts.W8,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static CostContext ContextOf(string name) => name == "W6" ? TestCostSheet.Context(capital: 100_000m, maxCostR: 0.20m) : TestCostSheet.Context();

    private static SimTrade Costed(SimTrade trade, CostContext? context = null, CostSegment segment = CostSegment.IntradayEquity)
    {
        var applied = TradeCostApplier.Apply(trade, context ?? TestCostSheet.Context(), segment);
        applied.IsSuccess.ShouldBeTrue(applied.IsFailure ? applied.Error.Message : string.Empty);
        return applied.Value.Trade.ShouldNotBeNull();
    }

    private static TradeSkip? Skipped(SimTrade trade, CostContext? context = null, CostSegment segment = CostSegment.IntradayEquity)
    {
        var applied = TradeCostApplier.Apply(trade, context ?? TestCostSheet.Context(), segment);
        applied.IsSuccess.ShouldBeTrue();
        applied.Value.Trade.ShouldBeNull();
        return applied.Value.Skip;
    }

    [Theory]
    [InlineData("W1")]
    [InlineData("W2")]
    [InlineData("W5")]
    [InlineData("W6")]
    [InlineData("W7A")]
    [InlineData("W7B")]
    [InlineData("W8")]
    public void The_worked_trades_equal_the_hand_figures_line_by_line(string name)
    {
        var kept = Costed(Trade(name), ContextOf(name));

        WorkedAssert.ShouldMatch(kept, Hand(name));
    }

    [Fact]
    public void The_worked_net_figures_are_the_ones_the_spec_prints()
    {
        Costed(CostTrades.W1()).Costing!.RNet.ShouldBe(1.80718, 1e-9);
        Costed(Trade("W2")).Costing!.RNet.ShouldBe(1.9171, 1e-4);
        Costed(Trade("W5")).Costing!.RNet.ShouldBe(1.81228, 1e-9);
        Costed(Trade("W6"), ContextOf("W6")).Costing!.RNet.ShouldBe(1.75444, 1e-9);
        Costed(Trade("W7A")).Costing!.RNet.ShouldBe(1.96029, 1e-9);
        Costed(Trade("W7B")).Costing!.RNet.ShouldBe(1.9624, 1e-9);
        Costed(Trade("W8")).Costing!.RNet.ShouldBe(-1.038314, 1e-9);
    }

    [Fact]
    public void W3_is_skipped_by_cost_W6_at_the_default_limit_is_skipped_and_W4_is_skipped_by_quantity()
    {
        Skipped(Trade("W3")).ShouldBe(TradeSkip.CostAboveMax);
        Skipped(Trade("W6"), TestCostSheet.Context(capital: 100_000m, maxCostR: 0.10m)).ShouldBe(TradeSkip.CostAboveMax);
        Skipped(CostTrades.Make(Side.Buy, Mar2, 3_000.00, 2_970.00, 3_060.00), TestCostSheet.Context(capital: 10_000m)).ShouldBe(TradeSkip.QuantityBelowOne);
    }

    [Fact]
    public void A_trade_whose_charge_in_R_equals_the_limit_is_kept_and_one_basis_point_of_a_tick_below_is_skipped()
    {
        Costed(CostTrades.W1(), TestCostSheet.Context(maxCostR: 0.09282m)).Costing!.TotalCost.ShouldBe(232.05m);
        Skipped(CostTrades.W1(), TestCostSheet.Context(maxCostR: 0.09281m)).ShouldBe(TradeSkip.CostAboveMax);
    }

    [Fact]
    public void A_trade_that_fails_both_checks_is_skipped_for_its_quantity_first()
    {
        // capital 10,000 and a price of 3,000: one share is over the 25 % cap (byCap 0.83), so the quantity is 0 whatever the charge would have been
        Skipped(CostTrades.Make(Side.Buy, Mar2, 3_000.00, 2_999.95, 3_000.10), TestCostSheet.Context(capital: 10_000m)).ShouldBe(TradeSkip.QuantityBelowOne);
    }

    [Fact]
    public void W5_costs_exactly_twelve_rupees_seventy_five_paise_less_than_W1_because_the_exit_STT_is_a_schedule_cheaper()
    {
        var w1 = Costed(CostTrades.W1()).Costing!;
        var w5 = Costed(CostTrades.W1(Mar3)).Costing!;

        (w1.TotalCost - w5.TotalCost).ShouldBe(12.75m);
        w5.EntryLeg.ShouldBe(w1.EntryLeg, "the entry leg is unchanged");
        (w1.ExitLeg.Stt - w5.ExitLeg.Stt).ShouldBe(12.75m);
    }

    [Fact]
    public void The_quantity_comes_from_the_exact_entry_and_not_from_the_filled_one()
    {
        var exact = CostTrades.Make(Side.Buy, Mar2, 100.00, 99.00, 102.00);
        var slipped = CostTrades.Make(Side.Buy, Mar2, 100.00, 99.00, 102.00, entryFilled: 100.35, exitFilled: 101.80);

        Costed(slipped).Costing!.Quantity.ShouldBe(Costed(exact).Costing!.Quantity);
        Costed(slipped).Costing!.EntryTurnover.ShouldBe(2500 * 100.35m, "the turnover is the filled price");
        Costed(slipped).Costing!.ExitTurnover.ShouldBe(2500 * 101.80m);
    }

    [Fact]
    public void A_sell_is_charged_on_its_own_sides_the_stt_on_its_entry_and_the_stamp_duty_on_its_exit_and_a_buy_the_reverse()
    {
        var sell = Costed(Trade("W2")).Costing!;
        var buy = Costed(CostTrades.W1()).Costing!;

        sell.EntryLeg.Stt.ShouldBeGreaterThan(0);
        sell.EntryLeg.StampDuty.ShouldBe(0m);
        sell.ExitLeg.StampDuty.ShouldBeGreaterThan(0);
        sell.ExitLeg.Stt.ShouldBe(0m);
        buy.EntryLeg.StampDuty.ShouldBeGreaterThan(0);
        buy.EntryLeg.Stt.ShouldBe(0m);
        buy.ExitLeg.Stt.ShouldBeGreaterThan(0);
        buy.ExitLeg.StampDuty.ShouldBe(0m);
    }

    [Fact]
    public void A_swing_shaped_trade_takes_the_entry_leg_from_the_earlier_schedule_the_exit_leg_from_the_later_and_the_dp_charge_on_the_sell_leg_only()
    {
        // L1: 100 shares bought at 1,000.00 on 2026-03-02 (schedule A) and sold at 1,050.00 on 2026-03-04 (schedule B); the profile lets one position cost the whole capital so that the quantity is 100
        var profile = SizingProfile.Create("test-wide", 2m, 100m).Value;
        var context = TestCostSheet.Context(capital: 100_000m, maxCostR: 0.50m, profile: profile);
        var trade = CostTrades.Make(Side.Buy, Mar2, 1_000.00, 990.00, 1_050.00, exitDate: new DateOnly(2026, 3, 4), reason: ExitReason.MaxHold);

        var costing = Costed(trade, context, CostSegment.Delivery).Costing!;

        costing.Quantity.ShouldBe(100);
        costing.Segment.ShouldBe(CostSegment.Delivery);
        costing.EntryLeg.ShouldBe(WorkedCosts.L1Buy.Charges);
        costing.ExitLeg.ShouldBe(WorkedCosts.L1Sell.Charges);
        costing.ExitLeg.Total.ShouldNotBe(WorkedCosts.L1SellOnA.Total, "charging the sell with the entry's schedule would give 99.58");
        costing.TotalCost.ShouldBe(225.39m);
        costing.EntryLeg.DpCharge.ShouldBe(0m);
        costing.ExitLeg.DpCharge.ShouldBe(10m);
    }

    [Fact]
    public void A_swing_shaped_trade_that_exits_on_its_entry_date_uses_one_schedule_for_both_legs()
    {
        var profile = SizingProfile.Create("test-wide", 2m, 100m).Value;
        var context = TestCostSheet.Context(capital: 100_000m, maxCostR: 0.50m, profile: profile);
        var trade = CostTrades.Make(Side.Buy, Mar3, 1_000.00, 990.00, 1_050.00);

        var costing = Costed(trade, context, CostSegment.Delivery).Costing!;

        costing.ExitLeg.Stt.ShouldBe(94.50m, "schedule B for both");
        costing.EntryLeg.Stt.ShouldBe(80.00m);
    }

    [Fact]
    public void A_trade_dated_before_the_first_schedule_fails_closed_and_is_not_a_zero_cost_trade()
    {
        var early = CostTrades.W1(new DateOnly(2025, 12, 31));
        var swingExit = CostTrades.Make(Side.Buy, new DateOnly(2026, 1, 2), 1_000.00, 990.00, 1_050.00, exitDate: new DateOnly(2026, 1, 5));
        var lateSheet = TestCostSheet.Of("test-late", TestCostSheet.Schedule(new DateOnly(2026, 1, 3), TestCostSheet.IntradayA, TestCostSheet.DeliveryA));

        var applied = TradeCostApplier.Apply(early, TestCostSheet.Context(), CostSegment.IntradayEquity);
        var lateEntry = TradeCostApplier.Apply(swingExit, TestCostSheet.Context(sheet: lateSheet), CostSegment.IntradayEquity);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe(CostErrors.NoRatesForDateCode);
        lateEntry.IsFailure.ShouldBeTrue("the entry leg is before the first schedule even though the exit leg is not");
        lateEntry.Error.Code.ShouldBe(CostErrors.NoRatesForDateCode);
    }

    [Fact]
    public void A_trade_with_no_risk_is_a_caller_error_reported_as_a_sizing_input_and_never_a_division_by_zero()
    {
        var applied = TradeCostApplier.Apply(CostTrades.Make(Side.Buy, Mar2, 100.00, 100.00, 101.00), TestCostSheet.Context(), CostSegment.IntradayEquity);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe(CostErrors.SizingInputInvalidCode);
    }

    [Fact]
    public void A_trade_costed_twice_and_a_null_argument_are_caller_errors()
    {
        var kept = Costed(CostTrades.W1());

        Should.Throw<ArgumentException>(() => TradeCostApplier.Apply(kept, TestCostSheet.Context(), CostSegment.IntradayEquity));
        Should.Throw<ArgumentNullException>(() => TradeCostApplier.Apply(null!, TestCostSheet.Context(), CostSegment.IntradayEquity));
        Should.Throw<ArgumentNullException>(() => TradeCostApplier.Apply(CostTrades.W1(), null!, CostSegment.IntradayEquity));
    }

    [Fact]
    public void Every_other_field_of_a_kept_trade_is_the_walkers_byte_for_byte()
    {
        foreach (var name in new[] { "W1", "W2", "W5", "W7A", "W8" })
        {
            var gross = Trade(name);

            var kept = Costed(gross);

            (kept with { Costing = null, Costs = CostBasis.None }).ShouldBe(gross, name);
        }
    }

    [Fact]
    public void Net_R_is_below_filled_R_whenever_something_was_charged_and_equal_to_it_when_nothing_was()
    {
        var kept = Costed(CostTrades.W1());
        kept.Costing!.RNet.ShouldBeLessThan(kept.RFilled);
        kept.RFilled.ShouldBeLessThanOrEqualTo(kept.RExact);

        // two shares at 0.05: every line of the sheet rounds to 0.00 on a turnover of 10 paise, so the charge is 0 and net R is filled R exactly
        var tiny = CostTrades.Make(Side.Buy, Mar2, 0.05, 0.04, 0.07);
        var profile = SizingProfile.Create("test-one", 2m, 100m).Value;
        var context = TestCostSheet.Context(capital: 1m, maxCostR: 1m, profile: profile);
        var costed = Costed(tiny, context);

        costed.Costing!.Quantity.ShouldBe(2);
        costed.Costing.TotalCost.ShouldBe(0m);
        costed.Costing.CostR.ShouldBe(0d);
        costed.Costing.RNet.ShouldBe(costed.RFilled);
    }

    [Fact]
    public void A_stop_of_one_tick_is_sized_by_the_cap_and_skipped_by_cost_because_the_charge_dwarfs_the_risk()
    {
        // risk 0.05 per share: byRisk 1,00,000 shares, byCap 2,500: the quantity is 2,500, the risk taken only 125.00 and the charge about 230
        var oneTick = CostTrades.Make(Side.Buy, Mar2, 100.00, 99.95, 100.10);

        Skipped(oneTick).ShouldBe(TradeSkip.CostAboveMax);
        Costed(oneTick, TestCostSheet.Context(maxCostR: 100m)).Costing!.CostR.ShouldBeGreaterThan(1.0);
    }

    [Fact]
    public void A_price_of_five_paise_and_a_price_of_a_lakh_are_sized_and_charged()
    {
        var cheap = Costed(CostTrades.Make(Side.Buy, Mar2, 0.05, 0.04, 0.07), TestCostSheet.Context(capital: 100_000m, maxCostR: 100m));
        var dear = Costed(CostTrades.Make(Side.Buy, Mar2, 100_000.00, 99_000.00, 102_000.00), TestCostSheet.Context(capital: 100_000_000m, maxCostR: 1m));

        cheap.Costing!.Quantity.ShouldBe(50_000, "byRisk 500 / 0.01");
        dear.Costing!.Quantity.ShouldBe(250, "byRisk 500,000 / 1,000 = 500, byCap 25,000,000 / 100,000 = 250");
        dear.Costing.TotalCost.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_one_share_position_is_costed_like_any_other()
    {
        var context = TestCostSheet.Context(capital: 10_000m, maxCostR: 100m);
        var trade = CostTrades.Make(Side.Buy, Mar2, 2_400.00, 2_350.00, 2_500.00);

        var costing = Costed(trade, context).Costing!;

        costing.Quantity.ShouldBe(1);
        costing.RiskTaken.ShouldBe(50.00m);
        costing.EntryTurnover.ShouldBe(2_400.00m);
    }

    [Fact]
    public void The_applier_application_holds_exactly_one_of_a_trade_and_a_skip()
    {
        var kept = CostApplication.Kept(CostTrades.W1());
        var skipped = CostApplication.Skipped(TradeSkip.CostAboveMax);

        (kept.Trade is not null, kept.Skip is null).ShouldBe((true, true));
        (skipped.Trade is null, skipped.Skip == TradeSkip.CostAboveMax).ShouldBe((true, true));
        Should.Throw<ArgumentNullException>(() => CostApplication.Kept(null!));
        typeof(CostApplication).GetConstructors().ShouldBeEmpty();
    }
}
