using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// Criterion 9 of P4-02 (and the findings of the review of P4-01 on <see cref="TradeCostApplier"/>): the trades of two hand rows of the policies, BUY at 100.50 with the stop 98.00 (risk 2.50) on 2026-03-02, are charged one order for each leg on
/// the TEST sheet, to the paisa, and the figures of the legs add up to <c>TotalCost</c>, <c>CostR</c> and <c>RNet</c>. Sizing: risk 0.5 % of 10,00,000 = 5,000 / 2.50 = 2,000 shares (the cap of 25 % allows 2,487). Intraday rates of schedule A:
/// brokerage 0.03 % capped at 20, STT on a sell 0.04 %, exchange 0.005 %, SEBI 50 per crore, stamp 0.02 % on a buy, GST 18 % of brokerage, exchange and SEBI; each line is rounded to the paisa half away from zero.
/// </summary>
public sealed class TradeCostApplierPolicyRowsTests
{
    private static readonly DateOnly Mar2 = new(2026, 3, 2);

    private static Leg Entry() => new(Day.Epoch(Mar2, 585), 100.50, 100.50, 1m, LegKind.Entry, string.Empty);

    private static Leg Exit(int minute, double price, decimal share, string reason) => new(Day.Epoch(Mar2, minute), price, price, share, LegKind.Exit, reason);

    private static SimTrade Trade(double rExact, params Leg[] exits)
    {
        var plain = CostTrades.Make(Side.Buy, Mar2, 100.50, 98.00, exits[^1].Price, entryFilled: 100.50, exitFilled: exits[^1].Price);
        var legs = new List<Leg> { Entry() };
        legs.AddRange(exits);
        return plain with { RExact = rExact, RFilled = rExact, SlippageR = 0d, Legs = new ValueList<Leg>(legs) };
    }

    private static TradeCosting Costed(SimTrade trade)
    {
        var applied = TradeCostApplier.Apply(trade, TestCostSheet.Context(maxCostR: 0.20m), CostSegment.IntradayEquity);
        applied.IsSuccess.ShouldBeTrue(applied.IsFailure ? applied.Error.Message : string.Empty);
        return applied.Value.Trade.ShouldNotBeNull().Costing.ShouldNotBeNull();
    }

    [Fact]
    public void The_PARTIAL_BE_row_B1_T1_half_at_103_00_and_T2_the_rest_at_105_50_is_charged_one_order_for_each_leg()
    {
        // entry 2,000 x 100.50 = 201,000.00: brokerage min(60.30, 20) = 20.00; STT 0; exchange 10.05; SEBI 1.005 -> 1.01; stamp 40.20; GST 18 % x (20.00 + 10.05 + 1.01) = 5.5908 -> 5.59; total 76.85
        // T1: 1,000 x 103.00 = 103,000.00: brokerage 20.00; STT 41.20; exchange 5.15; SEBI 0.515 -> 0.52; GST 18 % x 25.67 = 4.6206 -> 4.62; total 71.49
        // T2: 1,000 x 105.50 = 105,500.00: brokerage 20.00; STT 42.20; exchange 5.275 -> 5.28; SEBI 0.5275 -> 0.53; GST 18 % x 25.81 = 4.6458 -> 4.65; total 72.66
        var costing = Costed(Trade(1.5, Exit(600, 103.00, 0.5m, "T1"), Exit(610, 105.50, 0.5m, "Target")));

        costing.Quantity.ShouldBe(2000);
        costing.LegCharges.ShouldNotBeNull().Select(c => (c.Quantity, c.Turnover, c.Charges.Total)).ShouldBe([(2000L, 201000.00m, 76.85m), (1000L, 103000.00m, 71.49m), (1000L, 105500.00m, 72.66m)]);
        costing.LegCharges[1].Charges.ShouldBe(new LegCharges(20.00m, 41.20m, 5.15m, 0.52m, 0m, 0m, 0m, 4.62m));
        costing.LegCharges[2].Charges.ShouldBe(new LegCharges(20.00m, 42.20m, 5.28m, 0.53m, 0m, 0m, 0m, 4.65m));
        costing.TotalCost.ShouldBe(221.00m);
        costing.RiskTaken.ShouldBe(5000.00m);
        costing.CostR.ShouldBe(0.0442, 1e-12);
        costing.RNet.ShouldBe(1.5 - 0.0442, 1e-12);
    }

    [Fact]
    public void The_PARTIAL_TRAIL_row_R1_T1_half_at_103_00_and_the_rest_at_the_trailed_102_00_is_charged_one_order_for_each_leg()
    {
        // T1 is as above (71.49); the rest: 1,000 x 102.00 = 102,000.00: brokerage 20.00; STT 40.80; exchange 5.10; SEBI 0.51; GST 18 % x 25.61 = 4.6098 -> 4.61; total 71.02
        var costing = Costed(Trade(0.8, Exit(600, 103.00, 0.5m, "T1"), Exit(620, 102.00, 0.5m, "Stop")));

        costing.LegCharges.ShouldNotBeNull().Select(c => (c.Quantity, c.Turnover, c.Charges.Total)).ShouldBe([(2000L, 201000.00m, 76.85m), (1000L, 103000.00m, 71.49m), (1000L, 102000.00m, 71.02m)]);
        costing.TotalCost.ShouldBe(219.36m);
        costing.CostR.ShouldBe(0.043872, 1e-12);
        costing.RNet.ShouldBe(0.8 - 0.043872, 1e-12);
    }

    [Fact]
    public void A_one_share_position_books_no_T1_order_because_its_leg_would_have_no_shares_and_the_merged_leg_is_the_difference_of_the_counts()
    {
        // a position of one share: floor(1 x 0.5) = 0 shares at T1, so the leg has no order; the last exit takes the one share. The replay counts LegsMerged as (legs - charged legs): here 3 - 2 = 1, for a leg that is not the last
        var trade = Trade(0.8, Exit(600, 103.00, 0.5m, "T1"), Exit(620, 102.00, 0.5m, "Stop"));
        var oneShare = TestCostSheet.Context(capital: 500m, maxCostR: 50m);

        var applied = TradeCostApplier.Apply(trade, oneShare, CostSegment.IntradayEquity);

        applied.IsSuccess.ShouldBeTrue(applied.IsFailure ? applied.Error.Message : string.Empty);
        var costing = applied.Value.Trade.ShouldNotBeNull().Costing.ShouldNotBeNull();
        costing.Quantity.ShouldBe(1);
        costing.LegCharges.ShouldNotBeNull().Select(c => c.Quantity).ShouldBe([1L, 1L]);
        (trade.Legs!.Count - costing.LegCharges.Count).ShouldBe(1, "one leg of no shares was merged and not charged");
    }

    [Fact]
    public void A_trade_whose_legs_have_no_exit_leg_cannot_be_costed_and_is_a_validation_error_not_an_exception()
    {
        var plain = CostTrades.Make(Side.Buy, Mar2, 100.50, 98.00, 102.00, entryFilled: 100.50, exitFilled: 102.00);
        var trade = plain with { Legs = new ValueList<Leg>([Entry()]) };

        var applied = TradeCostApplier.Apply(trade, TestCostSheet.Context(maxCostR: 0.20m), CostSegment.IntradayEquity);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe(CostErrors.LegsInvalidCode);
        applied.Error.Kind.ShouldBe(ErrorKind.Validation);
    }
}
