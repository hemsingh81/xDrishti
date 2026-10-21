using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 4 of P4-04: the charges of a pyramid are one order per leg, the entry, each add and the single exit, and the exit leg holds the sum of the quantities of the legs in (D10), by hand on the TEST sheet. The trade is the walk of the hand
/// row P25 (a buy stopped at 99.21875 with one add, filled 100.05, 101.30 and 98.95), 2,500 shares for the full size (the cap binds, as in W1 of P3-03c), on 2026-03-02 (schedule A): brokerage 0.03 % capped at 20, STT 0.04 % on a sell, exchange
/// 0.005 %, SEBI 50 per crore, stamp 0.02 % on a buy, GST 18 % of brokerage, exchange and SEBI, each line rounded to the paisa half away from zero.
/// </summary>
public sealed class PyramidCostTests
{
    private static PyramidRow Row() => new(
        "P25", Side.Buy, ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25", "10:20 101.25/101.25/99.20/99.30"], [new("10:00", 101.20, double.NaN), new("10:15", 101.25, double.NaN)],
        new PyramidWant(ExitReason.Stop, "10:20", 99.21875, -1.00, 1.25, 1.20, [], RFilled: -1.255, Taken: 1), Modelled: true);

    private static SimTrade Trade()
    {
        var walked = PyramidHand.Walk(Row());
        walked.IsSuccess.ShouldBeTrue();
        return walked.Value.Trade.ShouldNotBeNull();
    }

    [Fact]
    public void A_pyramid_with_an_add_is_charged_one_order_for_the_entry_one_for_the_add_and_one_for_the_exit_by_hand()
    {
        // quantities: Q = 2,500; entry floor(2,500 x 0.5) = 1,250; add floor(2,500 x 0.3) = 750; the exit leg takes 1,250 + 750 = 2,000
        // entry: BUY 1,250 x 100.05 = 125,062.50: brokerage min(37.51875, 20) = 20.00; STT 0; exchange 6.253125 -> 6.25; SEBI 0.6253125 -> 0.63; stamp 25.0125 -> 25.01; GST 18 % x (20.00 + 6.25 + 0.63) = 4.8384 -> 4.84; total 56.73
        // add: BUY 750 x 101.30 = 75,975.00: brokerage 20.00; exchange 3.79875 -> 3.80; SEBI 0.379875 -> 0.38; stamp 15.195 -> 15.20; GST 18 % x 24.18 = 4.3524 -> 4.35; total 43.73
        // exit: SELL 2,000 x 98.95 = 197,900.00: brokerage 20.00; STT 79.16; exchange 9.895 -> 9.90; SEBI 0.9895 -> 0.99; stamp 0; GST 18 % x 30.89 = 5.5602 -> 5.56; total 115.61
        // total 216.07; risk taken 2,500 x 1.00 = 2,500.00; CostR 216.07 / 2,500 = 0.086428; RNet -1.255 - 0.086428 = -1.341428
        var applied = TradeCostApplier.Apply(Trade(), TestCostSheet.Context(maxCostR: 0.20m), CostSegment.IntradayEquity);

        applied.IsSuccess.ShouldBeTrue(applied.IsFailure ? applied.Error.Message : string.Empty);
        var costing = applied.Value.Trade.ShouldNotBeNull().Costing.ShouldNotBeNull();
        costing.Quantity.ShouldBe(2500);
        var charges = costing.LegCharges.ShouldNotBeNull();
        charges.Count.ShouldBe(3);
        charges[0].Quantity.ShouldBe(1250);
        charges[0].Turnover.ShouldBe(125062.50m);
        charges[0].Charges.ShouldBe(new LegCharges(20.00m, 0m, 6.25m, 0.63m, 25.01m, 0m, 0m, 4.84m));
        charges[0].Charges.Total.ShouldBe(56.73m);
        charges[1].Quantity.ShouldBe(750);
        charges[1].Turnover.ShouldBe(75975.00m);
        charges[1].Charges.ShouldBe(new LegCharges(20.00m, 0m, 3.80m, 0.38m, 15.20m, 0m, 0m, 4.35m));
        charges[1].Charges.Total.ShouldBe(43.73m);
        charges[2].Quantity.ShouldBe(2000, "the exit leg holds the sum of the quantities of the legs in");
        charges[2].Turnover.ShouldBe(197900.00m);
        charges[2].Charges.ShouldBe(new LegCharges(20.00m, 79.16m, 9.90m, 0.99m, 0m, 0m, 0m, 5.56m));
        charges[2].Charges.Total.ShouldBe(115.61m);
        costing.EntryTurnover.ShouldBe(201037.50m);
        costing.ExitTurnover.ShouldBe(197900.00m);
        costing.TotalCost.ShouldBe(216.07m);
        costing.RiskTaken.ShouldBe(2500.00m);
        costing.CostR.ShouldBe(0.086428, 1e-12);
        costing.RNet.ShouldBe(-1.255 - 0.086428, 1e-12);
    }

    [Fact]
    public void A_pyramid_whose_first_leg_holds_no_whole_share_is_a_skipped_quantity()
    {
        // a capital of 400 gives Q = floor(min(400 x 0.5 % / 1.00, 400 x 25 % / 100.00)) = floor(min(2.0, 1.0)) = 1: the first leg floor(1 x 0.5) = 0
        var applied = TradeCostApplier.Apply(Trade(), TestCostSheet.Context(capital: 400m, maxCostR: 0.20m), CostSegment.IntradayEquity);

        applied.IsSuccess.ShouldBeTrue();
        applied.Value.Skip.ShouldBe(TradeSkip.QuantityBelowOne);
        applied.Value.Trade.ShouldBeNull();
    }
}
