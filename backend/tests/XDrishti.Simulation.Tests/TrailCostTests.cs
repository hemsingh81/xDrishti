using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 9 of P4-03: the charges of a trail trade are those of one entry order and one exit order (D9, ADR 0042), by hand on the TEST sheet. The trades are the walks of the hand rows A18 (an ATR trail stopped at 100.60, filled 100.05 and
/// 100.40) and E15 (an EMA exit filled 100.05 and 100.05), 2,500 shares each (the cap binds, as in W1 of P3-03c), on 2026-03-02 (schedule A): brokerage 0.03 % capped at 20, STT 0.04 % on a sell, exchange 0.005 %, SEBI 50 per crore, stamp
/// 0.02 % on a buy, GST 18 % of brokerage, exchange and SEBI, each line rounded to the paisa half away from zero.
/// </summary>
public sealed class TrailCostTests
{
    private static TradeCosting Costed(SimTrade trade)
    {
        var applied = TradeCostApplier.Apply(trade, TestCostSheet.Context(maxCostR: 0.20m), CostSegment.IntradayEquity);
        applied.IsSuccess.ShouldBeTrue(applied.IsFailure ? applied.Error.Message : string.Empty);
        return applied.Value.Trade.ShouldNotBeNull().Costing.ShouldNotBeNull();
    }

    private static SimTrade Trade(TrailRow row, IExitRule rule)
    {
        var walked = TrailHand.Walk(row, rule);
        walked.IsSuccess.ShouldBeTrue();
        return walked.Value.Trade.ShouldNotBeNull();
    }

    [Fact]
    public void An_ATR_trail_trade_is_charged_one_entry_order_and_one_exit_order_by_hand()
    {
        // entry: BUY 2,500 x 100.05 = 250,125.00: brokerage min(75.0375, 20) = 20.00; STT 0; exchange 12.50625 -> 12.51; SEBI 1.250625 -> 1.25; stamp 50.025 -> 50.03; GST 18 % x (20.00 + 12.51 + 1.25) = 6.0768 -> 6.08; total 89.87
        // exit: SELL 2,500 x 100.40 = 251,000.00: brokerage 20.00; STT 100.40; exchange 12.55; SEBI 1.255 -> 1.26; stamp 0; GST 18 % x (20.00 + 12.55 + 1.26) = 6.0858 -> 6.09; total 140.30
        // total 230.17; risk taken 2,500 x 1.00 = 2,500.00; CostR 230.17 / 2,500 = 0.092068; RNet 0.35 - 0.092068 = 0.257932
        var row = AtrRow();
        var costing = Costed(Trade(row, new AtrTrailRule(0, 2m, 1m)));

        costing.Quantity.ShouldBe(2500);
        costing.LegCharges.ShouldBeNull("a trail leaves in one fill: no legs, no per-leg list");
        costing.EntryTurnover.ShouldBe(250125.00m);
        costing.ExitTurnover.ShouldBe(251000.00m);
        costing.EntryLeg.ShouldBe(new LegCharges(20.00m, 0m, 12.51m, 1.25m, 50.03m, 0m, 0m, 6.08m));
        costing.EntryLeg.Total.ShouldBe(89.87m);
        costing.ExitLeg.ShouldBe(new LegCharges(20.00m, 100.40m, 12.55m, 1.26m, 0m, 0m, 0m, 6.09m));
        costing.ExitLeg.Total.ShouldBe(140.30m);
        costing.TotalCost.ShouldBe(230.17m);
        costing.RiskTaken.ShouldBe(2500.00m);
        costing.CostR.ShouldBe(0.092068, 1e-12);
        costing.RNet.ShouldBe(0.35 - 0.092068, 1e-12);
    }

    [Fact]
    public void An_EMA_exit_trade_is_charged_one_entry_order_and_one_exit_order_by_hand()
    {
        // entry as above: 89.87. exit: SELL 2,500 x 100.05 = 250,125.00: brokerage 20.00; STT 100.05; exchange 12.51; SEBI 1.25; GST 18 % x 33.76 = 6.0768 -> 6.08; total 139.89
        // total 229.76; CostR 229.76 / 2,500 = 0.091904; RFilled 0, so RNet -0.091904
        var costing = Costed(Trade(EmaRow(), new EmaTrailRule(0)));

        costing.Quantity.ShouldBe(2500);
        costing.EntryLeg.Total.ShouldBe(89.87m);
        costing.ExitLeg.ShouldBe(new LegCharges(20.00m, 100.05m, 12.51m, 1.25m, 0m, 0m, 0m, 6.08m));
        costing.ExitLeg.Total.ShouldBe(139.89m);
        costing.TotalCost.ShouldBe(229.76m);
        costing.CostR.ShouldBe(0.091904, 1e-12);
        costing.RNet.ShouldBe(-0.091904, 1e-12);
    }

    private static TrailRow AtrRow() => new(
        "A18", Side.Buy, ["09:59 100.00/100.40/100.00/100.40", "10:05 100.40/101.60/100.40/101.20", "10:20 101.20/101.20/100.60/100.70"],
        [new("10:00", 100.40, 0.5), new("10:15", 101.20, 0.5)], new TrailWant(ExitReason.Stop, "10:20", 100.60, 0.60, 0, 1.60, RFilled: 0.35), Modelled: true, Multiple: 2.0, Activate: 1.0);

    private static TrailRow EmaRow() => new(
        "E15", Side.Buy, ["09:50 100.00/100.50/100.00/100.50", "10:20 100.50/100.60/100.50/100.60", "10:29 100.60/100.60/100.20/100.20", "10:30 100.15/100.20/100.10/100.20"],
        [new("10:00", 100.50, 100.30), new("10:15", 100.50, 100.40), new("10:30", 100.20, 100.40)], new TrailWant(ExitReason.EmaExit, "10:30", 100.15, 0.15, 0, 0.60, RFilled: 0.0), Modelled: true);
}
