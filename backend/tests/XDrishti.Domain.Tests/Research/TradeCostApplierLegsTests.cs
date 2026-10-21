using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// Criterion 8 of P4-01 (D11): a trade with legs is charged one order for each leg, on the TEST sheet, by hand. Base: W1 of P3-03c, BUY 2,500 shares (quantity 2,500, the cap binds), entry 100.00 filled 100.10, stop 99.00, risk 1.00, on
/// 2026-03-02 (schedule A). Intraday rates of schedule A: brokerage 0.03 % capped at 20, STT on a sell 0.04 %, exchange 0.005 %, SEBI 50 per crore, stamp 0.02 % on a buy, GST 18 % of brokerage, exchange and SEBI; each line is rounded to the paisa
/// half away from zero. The entry order is W1's (89.89 on 250,250.00).
/// </summary>
public sealed class TradeCostApplierLegsTests
{
    private static readonly DateOnly Mar2 = new(2026, 3, 2);

    private static readonly DateOnly Mar3 = new(2026, 3, 3);

    private static Leg Entry() => new(Day.Epoch(Mar2, 600), 100.00, 100.10, 1m, LegKind.Entry, string.Empty);

    private static Leg Exit(DateOnly date, int minute, double filled, decimal share, string reason = "LADDER") => new(Day.Epoch(date, minute), filled, filled, share, LegKind.Exit, reason);

    private static SimTrade Trade(params Leg[] exits)
    {
        var plain = CostTrades.Make(Side.Buy, Mar2, 100.00, 99.00, 102.00, entryFilled: 100.10, exitFilled: 102.00);
        var legs = new List<Leg> { Entry() };
        legs.AddRange(exits);
        var rFilled = exits.Sum(l => (double)l.Share * (l.FilledPrice - 100.10));
        return plain with { RFilled = rFilled, SlippageR = rFilled - plain.RExact, Legs = new ValueList<Leg>(legs) };
    }

    private static TradeCosting Costed(SimTrade trade, decimal maxCostR = 0.20m)
    {
        var applied = TradeCostApplier.Apply(trade, TestCostSheet.Context(maxCostR: maxCostR), CostSegment.IntradayEquity);
        applied.IsSuccess.ShouldBeTrue(applied.IsFailure ? applied.Error.Message : string.Empty);
        return applied.Value.Trade.ShouldNotBeNull().Costing.ShouldNotBeNull();
    }

    [Fact]
    public void Two_exit_orders_of_1000_and_1500_shares_are_charged_one_order_each_and_add_up_to_the_totals_by_hand()
    {
        // leg A: 40 % = 1,000 shares at 101.00 -> turnover 101,000.00: brokerage min(30.30, 20) = 20.00; STT 40.40; exchange 5.05; SEBI 0.505 -> 0.51; GST 18 % x (20.00 + 5.05 + 0.51) = 4.6008 -> 4.60; total 70.56
        // leg B: the rest = 1,500 shares at 102.00 -> turnover 153,000.00: brokerage 20.00; STT 61.20; exchange 7.65; SEBI 0.765 -> 0.77; GST 18 % x 28.42 = 5.1156 -> 5.12; total 94.74
        var costing = Costed(Trade(Exit(Mar2, 620, 101.00, 0.4m), Exit(Mar2, 640, 102.00, 0.6m, "Target")));

        costing.Quantity.ShouldBe(2500);
        costing.LegCharges.ShouldNotBeNull().Select(c => (c.Quantity, c.Turnover, c.Charges.Total)).ShouldBe([(2500L, 250250.00m, 89.89m), (1000L, 101000.00m, 70.56m), (1500L, 153000.00m, 94.74m)]);
        costing.LegCharges[1].Charges.ShouldBe(new LegCharges(20.00m, 40.40m, 5.05m, 0.51m, 0m, 0m, 0m, 4.60m));
        costing.LegCharges[2].Charges.ShouldBe(new LegCharges(20.00m, 61.20m, 7.65m, 0.77m, 0m, 0m, 0m, 5.12m));
        costing.EntryLeg.Total.ShouldBe(89.89m);
        costing.ExitLeg.ShouldBe(new LegCharges(40.00m, 101.60m, 12.70m, 1.28m, 0m, 0m, 0m, 9.72m));
        costing.ExitLeg.Total.ShouldBe(165.30m);
        costing.EntryTurnover.ShouldBe(250250.00m);
        costing.ExitTurnover.ShouldBe(254000.00m);
        costing.TotalCost.ShouldBe(255.19m);
        costing.RiskTaken.ShouldBe(2500.00m);
        costing.CostR.ShouldBe(0.102076, 1e-12);
        costing.RNet.ShouldBe(1.5 - 0.102076, 1e-12);
    }

    [Fact]
    public void The_quantities_of_the_legs_sum_exactly_and_the_remainder_goes_to_the_last_exit()
    {
        // three thirds of 2,500: floor(833.33) = 833, 833 and the rest 834
        var third = 1m / 3m;
        var costing = Costed(Trade(Exit(Mar2, 620, 101.00, third), Exit(Mar2, 630, 101.50, third), Exit(Mar2, 640, 102.00, 1m - third - third, "Target")));

        var charged = costing.LegCharges.ShouldNotBeNull();
        charged.Select(c => c.Quantity).ShouldBe([2500L, 833L, 833L, 834L]);
        charged.Skip(1).Sum(c => c.Quantity).ShouldBe(costing.Quantity);
    }

    [Fact]
    public void A_leg_that_would_have_no_shares_is_merged_into_the_next_and_not_charged_so_the_total_is_that_of_one_exit_order()
    {
        // 0.0001 of 2,500 is 0.25 shares: floor 0. The last exit takes all 2,500: the charges are W1's (89.89 + 142.16 = 232.05)
        var trade = Trade(Exit(Mar2, 620, 101.00, 0.0001m), Exit(Mar2, 640, 102.00, 0.9999m, "Target"));

        var costing = Costed(trade, 0.10m);

        costing.LegCharges.ShouldNotBeNull().Count.ShouldBe(2, "the leg of no shares has no order");
        trade.Legs.ShouldNotBeNull().Count.ShouldBe(3);
        costing.LegCharges.Select(c => c.Quantity).ShouldBe([2500L, 2500L]);
        costing.ExitLeg.Total.ShouldBe(142.16m);
        costing.TotalCost.ShouldBe(232.05m);
        costing.CostR.ShouldBe(0.09282, 1e-12);
    }

    [Fact]
    public void Each_leg_is_charged_on_the_schedule_in_force_on_its_own_date()
    {
        // leg B is on 2026-03-03 (schedule B: the intraday STT on a sell is 0.035 %): 153,000.00 x 0.035 % = 53.55; brokerage 20.00, exchange 7.65, SEBI 0.77, GST 5.12: 87.09
        var costing = Costed(Trade(Exit(Mar2, 620, 101.00, 0.4m), Exit(Mar3, 560, 102.00, 0.6m, "Target")));

        var charged = costing.LegCharges.ShouldNotBeNull();
        charged[1].Charges.Stt.ShouldBe(40.40m, "schedule A on 2026-03-02");
        charged[2].Charges.Stt.ShouldBe(53.55m, "schedule B on 2026-03-03");
        charged[2].Charges.Total.ShouldBe(87.09m);
        costing.TotalCost.ShouldBe(89.89m + 70.56m + 87.09m);
    }

    [Fact]
    public void A_trade_without_legs_is_costed_as_before_and_carries_no_leg_charges()
    {
        var costing = Costed(CostTrades.W1(), 0.10m);

        costing.LegCharges.ShouldBeNull();
        costing.TotalCost.ShouldBe(232.05m);
    }

    [Fact]
    public void A_charge_in_R_above_the_limit_skips_the_whole_trade_not_a_leg()
    {
        var trade = Trade(Exit(Mar2, 620, 101.00, 0.4m), Exit(Mar2, 640, 102.00, 0.6m, "Target"));

        var applied = TradeCostApplier.Apply(trade, TestCostSheet.Context(maxCostR: 0.10m), CostSegment.IntradayEquity);

        applied.Value.Trade.ShouldBeNull();
        applied.Value.Skip.ShouldBe(TradeSkip.CostAboveMax);
    }
}
