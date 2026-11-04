using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// CF-05 criterion 1: the charge hand examples of the spec, through the existing <see cref="CostModel"/> against a TEST sheet that holds the spec's numbers (never real rates): intraday at Rs 1,00,000 per leg is 82.4452 (0.0824 R at 1R = Rs 1,000, 0.2748 R at 1R = Rs 300),
/// the same trade as delivery 238.1752 (0.2382 R). The model rounds each line of each leg to the paisa, so its total is within a paisa of the unrounded hand figure.
/// </summary>
public sealed class CostStressChargeExamplesTests
{
    private static readonly DateOnly Day = new(2026, 3, 2);

    private static CostSheet Sheet()
    {
        var intraday = ChargeRates.Create(0.03m, 20m, 0m, 0m, 0.025m, 0.00297m, 10m, 0.003m, 0m, 0m).Value;
        var delivery = ChargeRates.Create(0m, 0m, 0m, 0.1m, 0.1m, 0.00297m, 10m, 0.015m, 0m, 15.93m).Value;
        var schedule = ChargeSchedule.Create(new DateOnly(2026, 1, 1), 18m, ChargeRounding.HalfAwayFromZero, intraday, delivery).Value;
        return CostSheet.Create("test-cf05-spec", "TEST sheet (the numbers of the CF-05 hand example)", "invented for tests; not a broker's sheet", null, [schedule]).Value;
    }

    private static decimal Total(CostSegment segment)
    {
        var sheet = Sheet();
        var buy = CostModel.Leg(sheet, segment, Side.Buy, 100_000m, Day).Value;
        var sell = CostModel.Leg(sheet, segment, Side.Sell, 100_000m, Day).Value;
        return buy.Total + sell.Total;
    }

    [Fact]
    public void Intraday_costs_82_44_five_lines_by_hand_and_is_0_0824_r_at_a_one_percent_stop()
    {
        var sheet = Sheet();
        var buy = CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Buy, 100_000m, Day).Value;
        var sell = CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Sell, 100_000m, Day).Value;

        (buy.Brokerage + sell.Brokerage).ShouldBe(40m, "Rs 20 + Rs 20: 0.03 % of Rs 1,00,000 is Rs 30 and the Rs 20 cap applies");
        (buy.Stt + sell.Stt).ShouldBe(25m);
        (buy.ExchangeTxn + sell.ExchangeTxn).ShouldBe(5.94m);
        (buy.SebiFee + sell.SebiFee).ShouldBe(0.20m);
        (buy.StampDuty + sell.StampDuty).ShouldBe(3.00m);
        Math.Abs((buy.Gst + sell.Gst) - 8.3052m).ShouldBeLessThanOrEqualTo(0.01m, "18 % of (40 + 5.94 + 0.20), rounded per leg");
        Math.Abs(Total(CostSegment.IntradayEquity) - 82.4452m).ShouldBeLessThanOrEqualTo(0.01m);
        ((double)Total(CostSegment.IntradayEquity) / 1000).ShouldBe(0.0824, 1e-3);
    }

    [Fact]
    public void The_same_trade_with_a_thirty_basis_point_stop_is_0_2748_r()
    {
        ((double)Total(CostSegment.IntradayEquity) / 300).ShouldBe(0.2748, 1e-3);
    }

    [Fact]
    public void Delivery_with_no_brokerage_costs_238_17_with_the_dp_charge_and_is_0_2382_r()
    {
        var sheet = Sheet();
        var buy = CostModel.Leg(sheet, CostSegment.Delivery, Side.Buy, 100_000m, Day).Value;
        var sell = CostModel.Leg(sheet, CostSegment.Delivery, Side.Sell, 100_000m, Day).Value;

        (buy.Stt + sell.Stt).ShouldBe(200m);
        (buy.StampDuty + sell.StampDuty).ShouldBe(15m);
        (buy.ExchangeTxn + sell.ExchangeTxn).ShouldBe(5.94m);
        (buy.SebiFee + sell.SebiFee).ShouldBe(0.20m);
        (buy.DpCharge + sell.DpCharge).ShouldBe(15.93m);
        Math.Abs((buy.Gst + sell.Gst) - 1.1052m).ShouldBeLessThanOrEqualTo(0.01m);
        Math.Abs(Total(CostSegment.Delivery) - 238.1752m).ShouldBeLessThanOrEqualTo(0.01m);
        ((double)Total(CostSegment.Delivery) / 1000).ShouldBe(0.2382, 1e-3);
    }

    [Fact]
    public void The_sheet_is_a_test_sheet_and_unchecked()
    {
        Sheet().IsTest.ShouldBeTrue();
        Sheet().Checked.ShouldBeNull();
    }
}
