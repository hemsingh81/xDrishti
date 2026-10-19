namespace XDrishti.Simulation.Tests.Oracle;

public sealed class CostRuleTests
{
    private static OracleSchedule Sched(OracleRates intraday, OracleRounding m = OracleRounding.HalfAwayFromZero, OracleRates? delivery = null) =>
        new(new DateOnly(2026, 1, 1), 18m, m, intraday, delivery ?? intraday);

    private static readonly OracleRates Zero = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    [Theory]
    [InlineData(10000, 3.00)]
    [InlineData(1000000, 20.00)]
    public void Brokerage_is_min_of_cap_and_max_of_min_and_percent(decimal t, decimal want)
    {
        var s = Sched(Zero with { BrokeragePct = 0.03m, BrokerageCap = 20m });
        ReferenceCosting.Leg(t, OracleSide.Buy, OracleCostSegment.IntradayEquity, s).Brokerage.ShouldBe(want);
    }

    [Fact]
    public void Brokerage_minimum_applies_below_it_and_a_cap_of_zero_means_no_brokerage()
    {
        var s = Sched(Zero with { BrokeragePct = 0.03m, BrokerageCap = 20m, BrokerageMin = 5m });
        ReferenceCosting.Leg(10000m, OracleSide.Sell, OracleCostSegment.IntradayEquity, s).Brokerage.ShouldBe(5m);
        var none = Sched(Zero);
        ReferenceCosting.Leg(10_000_000m, OracleSide.Sell, OracleCostSegment.IntradayEquity, none).Brokerage.ShouldBe(0m);
    }

    [Fact]
    public void A_flat_fee_is_percent_zero_with_min_equal_to_cap()
    {
        var s = Sched(Zero with { BrokerageCap = 12m, BrokerageMin = 12m });
        ReferenceCosting.Leg(500m, OracleSide.Buy, OracleCostSegment.IntradayEquity, s).Brokerage.ShouldBe(12m);
        ReferenceCosting.Leg(5_000_000m, OracleSide.Buy, OracleCostSegment.IntradayEquity, s).Brokerage.ShouldBe(12m);
    }

    [Fact]
    public void Stt_is_charged_on_the_side_the_sheet_names()
    {
        var s = Sched(Zero with { SttBuyPct = 0.1m, SttSellPct = 0.2m });
        ReferenceCosting.Leg(100000m, OracleSide.Buy, OracleCostSegment.IntradayEquity, s).Stt.ShouldBe(100m);
        ReferenceCosting.Leg(100000m, OracleSide.Sell, OracleCostSegment.IntradayEquity, s).Stt.ShouldBe(200m);
        var sellOnly = Sched(Zero with { SttSellPct = 0.2m });
        ReferenceCosting.Leg(100000m, OracleSide.Buy, OracleCostSegment.IntradayEquity, sellOnly).Stt.ShouldBe(0m);
    }

    [Fact]
    public void Exchange_and_sebi_are_charged_on_both_sides()
    {
        var s = Sched(Zero with { ExchangePct = 0.01m, SebiPerCrore = 100m });
        foreach (OracleSide side in new[] { OracleSide.Buy, OracleSide.Sell })
        {
            OracleLegLines l = ReferenceCosting.Leg(1_000_000m, side, OracleCostSegment.IntradayEquity, s);
            l.Exchange.ShouldBe(100m);
            l.Sebi.ShouldBe(10m);
        }
    }

    [Fact]
    public void Stamp_duty_is_on_buy_orders_only()
    {
        var s = Sched(Zero with { StampBuyPct = 0.02m });
        ReferenceCosting.Leg(100000m, OracleSide.Buy, OracleCostSegment.IntradayEquity, s).Stamp.ShouldBe(20m);
        ReferenceCosting.Leg(100000m, OracleSide.Sell, OracleCostSegment.IntradayEquity, s).Stamp.ShouldBe(0m);
    }

    [Fact]
    public void The_dp_charge_is_on_a_delivery_sell_only_and_carries_no_gst()
    {
        var s = Sched(Zero, delivery: Zero with { DpChargePerSell = 10m });
        ReferenceCosting.Leg(1000m, OracleSide.Sell, OracleCostSegment.Delivery, s).Dp.ShouldBe(10m);
        ReferenceCosting.Leg(1000m, OracleSide.Sell, OracleCostSegment.Delivery, s).Gst.ShouldBe(0m);
        ReferenceCosting.Leg(1000m, OracleSide.Buy, OracleCostSegment.Delivery, s).Dp.ShouldBe(0m);
        ReferenceCosting.Leg(1000m, OracleSide.Sell, OracleCostSegment.IntradayEquity, Sched(Zero with { DpChargePerSell = 10m }, delivery: Zero)).Dp.ShouldBe(0m);
    }

    [Fact]
    public void Other_is_a_share_of_turnover_on_both_sides()
    {
        var s = Sched(Zero with { OtherPct = 0.001m });
        ReferenceCosting.Leg(1_000_000m, OracleSide.Buy, OracleCostSegment.IntradayEquity, s).Other.ShouldBe(10m);
        ReferenceCosting.Leg(1_000_000m, OracleSide.Sell, OracleCostSegment.IntradayEquity, s).Other.ShouldBe(10m);
    }

    [Fact]
    public void Gst_base_is_the_rounded_brokerage_exchange_and_sebi_not_stt_stamp_other_or_dp()
    {
        var r = Zero with { BrokeragePct = 0.03m, BrokerageCap = 20m, SttSellPct = 0.5m, StampBuyPct = 0.5m, OtherPct = 0.5m, ExchangePct = 0.005m, SebiPerCrore = 50m, DpChargePerSell = 10m };
        var s = Sched(r, delivery: r);
        OracleLegLines l = ReferenceCosting.Leg(250250m, OracleSide.Sell, OracleCostSegment.Delivery, s);
        // base = 20.00 + 12.51 + 1.25 = 33.76 -> 6.0768 -> 6.08
        l.Gst.ShouldBe(6.08m);
    }

    [Theory]
    [InlineData(OracleRounding.HalfAwayFromZero, 1.28)]
    [InlineData(OracleRounding.HalfEven, 1.28)]
    [InlineData(OracleRounding.Down, 1.27)]
    public void The_three_roundings_on_1_275(OracleRounding mode, decimal want) =>
        ReferenceCosting.Round(1.275m, mode).ShouldBe(want);

    [Theory]
    [InlineData(OracleRounding.HalfAwayFromZero, 5.01)]
    [InlineData(OracleRounding.HalfEven, 5.00)]
    [InlineData(OracleRounding.Down, 5.00)]
    public void The_three_roundings_on_5_005(OracleRounding mode, decimal want) =>
        ReferenceCosting.Round(5.005m, mode).ShouldBe(want);

    [Fact]
    public void Each_line_is_rounded_once_before_the_leg_total_is_summed()
    {
        // exchange 4.90 (exactly), sebi 0.49; GST is 18 % of the rounded 5.39 = 0.9702 -> 0.97 (once); total 6.36.
        var s = Sched(Zero with { ExchangePct = 0.0049m, SebiPerCrore = 49m });
        OracleLegLines l = ReferenceCosting.Leg(100000m, OracleSide.Buy, OracleCostSegment.IntradayEquity, s);
        l.Exchange.ShouldBe(4.90m);
        l.Sebi.ShouldBe(0.49m);
        l.Gst.ShouldBe(0.97m);
        l.Total.ShouldBe(6.36m);
    }

    [Fact]
    public void Quantity_is_floored_never_rounded_and_has_a_minimum_of_one_by_skip()
    {
        // riskAmount 5,000 / risk 3 = 1666.67 ; cap 250,000 / 100 = 2500 -> 1666.
        ReferenceCosting.Quantity(1_000_000m, OracleSizing.Standard, 100m, 97m).ShouldBe(1666);
        // byCap 0.8333 -> 0 (a zero is a skip, not a rounding up)
        ReferenceCosting.Quantity(10_000m, OracleSizing.Standard, 3000m, 2970m).ShouldBe(0);
        ReferenceCosting.Quantity(10_000m, OracleSizing.Standard, 2500m, 2470m).ShouldBe(1);
    }

    [Fact]
    public void Quantity_is_taken_from_the_exact_entry_not_the_filled_one()
    {
        OracleCostInput t = CostExamples.All.Single(e => e.Id == "W1").Input;
        OracleCostResult a = ReferenceCosting.Cost(t, TestSheet.Sheet, OracleSizing.Standard);
        OracleCostResult b = ReferenceCosting.Cost(t with { EntryFilled = 100.50m }, TestSheet.Sheet, OracleSizing.Standard);
        a.Quantity.ShouldBe(b.Quantity);
        a.RiskTaken.ShouldBe(b.RiskTaken);
    }

    [Fact]
    public void A_quantity_input_that_is_not_above_zero_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ReferenceCosting.Quantity(0m, OracleSizing.Standard, 100m, 99m));
        Should.Throw<ArgumentOutOfRangeException>(() => ReferenceCosting.Quantity(1000m, OracleSizing.Standard, 100m, 100m));
    }

    [Fact]
    public void The_quantity_skip_is_decided_before_the_cost_skip()
    {
        // Quantity 0 and a limit of 0: the skip is Quantity.
        OracleCostInput t = CostExamples.All.Single(e => e.Id == "W4").Input with { MaxCostR = 0m };
        ReferenceCosting.Cost(t, TestSheet.Sheet, OracleSizing.Standard).Skip.ShouldBe(OracleCostSkip.Quantity);
    }

    [Fact]
    public void Net_r_is_r_filled_minus_cost_r_and_never_above_r_filled()
    {
        OracleCostResult r = ReferenceCosting.Cost(CostExamples.All.Single(e => e.Id == "W1").Input, TestSheet.Sheet, OracleSizing.Standard);
        r.RNet.ShouldBe(1.9 - r.CostRDouble);
        r.RNet!.Value.ShouldBeLessThan(1.9);
    }

    [Fact]
    public void A_zero_total_leaves_net_r_equal_to_r_filled_with_zero_stored_as_positive_zero()
    {
        OracleSchedule s = Sched(Zero);
        var sheet = new OracleChargeSheet("t", [s]);
        OracleCostInput t = CostExamples.All.Single(e => e.Id == "W1").Input with { RFilled = 0.0, RExact = 0.0 };
        OracleCostResult r = ReferenceCosting.Cost(t, sheet, OracleSizing.Standard);
        r.TotalCost.ShouldBe(0m);
        double.IsPositive(r.RNet!.Value).ShouldBeTrue();
        double.IsPositive(r.CostRDouble).ShouldBeTrue();
    }

    [Fact]
    public void The_schedule_in_force_is_the_last_one_on_or_before_the_date_and_none_before_the_first()
    {
        TestSheet.Sheet.In(new DateOnly(2025, 12, 31)).ShouldBeNull();
        TestSheet.Sheet.In(new DateOnly(2026, 1, 1))!.EffectiveFrom.ShouldBe(new DateOnly(2026, 1, 1));
        TestSheet.Sheet.In(new DateOnly(2026, 3, 2))!.EffectiveFrom.ShouldBe(new DateOnly(2026, 1, 1));
        TestSheet.Sheet.In(new DateOnly(2026, 3, 3))!.EffectiveFrom.ShouldBe(new DateOnly(2026, 3, 3));
    }

    [Fact]
    public void A_date_before_the_first_schedule_is_an_error_never_a_zero_cost_trade()
    {
        OracleCostInput t = CostExamples.All.Single(e => e.Id == "W1").Input with { EntryDate = new DateOnly(2025, 6, 1), ExitDate = new DateOnly(2025, 6, 1) };
        Should.Throw<InvalidOperationException>(() => ReferenceCosting.Cost(t, TestSheet.Sheet, OracleSizing.Standard))
            .Message.ShouldStartWith("costs.no_rates_for_date");
    }

    [Fact]
    public void Each_leg_uses_the_schedule_of_its_own_date()
    {
        OracleCostInput t = CostExamples.All.Single(e => e.Id == "L1").Input;
        OracleCostResult r = ReferenceCosting.Cost(t, TestSheet.Sheet, OracleSizing.Standard);
        r.EntryLeg!.Stt.ShouldBe(80.00m);
        r.ExitLeg!.Stt.ShouldBe(94.50m);
        r.Trace.ShouldContain("C-DATE-CHANGE");
        // The sell leg on the entry's schedule would give 84.00 and a total of 99.58 (CostFaultTests holds that line).
    }

    [Fact]
    public void A_cost_equal_to_the_limit_is_kept_and_one_paisa_under_it_is_skipped()
    {
        OracleCostInput t = CostExamples.All.Single(e => e.Id == "W1").Input;
        ReferenceCosting.Cost(t with { MaxCostR = 0.09282m }, TestSheet.Sheet, OracleSizing.Standard).Skip.ShouldBe(OracleCostSkip.None);
        ReferenceCosting.Cost(t with { MaxCostR = 0.09281m }, TestSheet.Sheet, OracleSizing.Standard).Skip.ShouldBe(OracleCostSkip.Cost);
    }

    [Fact]
    public void Every_line_is_non_negative_and_the_total_does_not_fall_when_turnover_rises()
    {
        decimal previous = 0m;
        foreach (decimal t in new[] { 100m, 1000m, 12345.67m, 250250m, 5_000_000m })
        {
            OracleLegLines l = ReferenceCosting.Leg(t, OracleSide.Sell, OracleCostSegment.Delivery, TestSheet.Sheet.Schedules[1]);
            new[] { l.Brokerage, l.Stt, l.Exchange, l.Sebi, l.Stamp, l.Other, l.Dp, l.Gst }.ShouldAllBe(x => x >= 0m);
            l.Total.ShouldBeGreaterThanOrEqualTo(previous);
            previous = l.Total;
        }
    }

    [Fact]
    public void The_named_limit_swing_gate_before_costing_is_the_last_of_ten_and_a_skipped_trade_still_counts_for_the_gate()
    {
        OracleCostLimits.NotModelled.Count.ShouldBe(10);
        OracleCostLimits.NotModelled[9].Code.ShouldBe("swing_gate_before_costing");
        OracleCostLimits.NotModelled.Select(l => l.Code).Distinct().Count().ShouldBe(10);
        OracleCostLimits.TradeCountsForTheGate(OracleCostSkip.Cost).ShouldBeTrue();
        OracleCostLimits.TradeCountsForTheGate(OracleCostSkip.Quantity).ShouldBeTrue();
        // The skipped trade is still a position: costing has no effect on the gate's input, so the same trade
        // is skipped by cost with one limit and kept with another and the position it occupies is the same.
        OracleCostInput t = CostExamples.All.Single(e => e.Id == "W3").Input;
        ReferenceCosting.Cost(t, TestSheet.Sheet, OracleSizing.Standard).Skip.ShouldBe(OracleCostSkip.Cost);
        ReferenceCosting.Cost(t with { MaxCostR = 1m }, TestSheet.Sheet, OracleSizing.Standard).Skip.ShouldBe(OracleCostSkip.None);
    }
}
