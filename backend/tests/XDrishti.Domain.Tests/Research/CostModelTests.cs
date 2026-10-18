using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// Criteria 4 to 7 of P3-03c: the eight lines of a leg equal the hand figures to the paisa, one by one; the rounding modes and the trap of a <c>double</c>; the branches of the brokerage; and the properties of the model on a grid and on
/// 2,000 seeded legs. The figures are in <see cref="WorkedCosts"/> as literals, worked out by hand before the code existed.
/// </summary>
public sealed class CostModelTests
{
    private static readonly DateOnly Mar2 = new(2026, 3, 2);

    private static readonly DateOnly Mar3 = new(2026, 3, 3);

    public static TheoryData<string> Cases() => [.. WorkedCosts.Legs.Select(l => l.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_line_of_the_leg_total_and_group_equals_the_printed_figure_to_the_paisa(string name)
    {
        var leg = WorkedCosts.Legs.Single(l => l.Name == name);
        var charged = CostModel.Leg(TestCostSheet.Sheet, leg.Segment, leg.Side, leg.Turnover, leg.Date);

        charged.IsSuccess.ShouldBeTrue();
        var lines = charged.Value;
        var hand = leg.Expected;
        lines.Brokerage.ShouldBe(hand.Brokerage, "brokerage");
        lines.Stt.ShouldBe(hand.Stt, "stt");
        lines.ExchangeTxn.ShouldBe(hand.Exchange, "exchange");
        lines.SebiFee.ShouldBe(hand.Sebi, "sebi");
        lines.StampDuty.ShouldBe(hand.Stamp, "stamp duty");
        lines.OtherCharges.ShouldBe(hand.Other, "other");
        lines.DpCharge.ShouldBe(hand.Dp, "dp charge");
        lines.Gst.ShouldBe(hand.Gst, "gst");
        lines.Total.ShouldBe(hand.Total, "total");
    }

    [Fact]
    public void The_lines_of_both_legs_of_each_worked_trade_sum_to_the_printed_group_figures()
    {
        // W1 by group, as the spec prints them: brokerage 40.00, STT 102.00, exchange 25.26, SEBI 2.53, stamp 50.05, GST 12.21
        var entry = CostModel.Leg(TestCostSheet.Sheet, CostSegment.IntradayEquity, Side.Buy, 250250.00m, Mar2).Value;
        var exit = CostModel.Leg(TestCostSheet.Sheet, CostSegment.IntradayEquity, Side.Sell, 255000.00m, Mar2).Value;

        (entry.Brokerage + exit.Brokerage).ShouldBe(40.00m);
        (entry.Stt + exit.Stt).ShouldBe(102.00m);
        (entry.ExchangeTxn + exit.ExchangeTxn).ShouldBe(25.26m);
        (entry.SebiFee + exit.SebiFee).ShouldBe(2.53m);
        (entry.StampDuty + exit.StampDuty).ShouldBe(50.05m);
        (entry.Gst + exit.Gst).ShouldBe(12.21m);
        (entry.Total + exit.Total).ShouldBe(232.05m);
    }

    [Fact]
    public void Stamp_duty_is_on_buy_orders_only_the_dp_charge_on_delivery_sells_only_and_the_stt_side_follows_the_sheet()
    {
        var sheet = TestCostSheet.Sheet;

        var intradayBuy = CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Buy, 100_000m, Mar2).Value;
        var intradaySell = CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Sell, 100_000m, Mar2).Value;
        var deliveryBuy = CostModel.Leg(sheet, CostSegment.Delivery, Side.Buy, 100_000m, Mar2).Value;
        var deliverySell = CostModel.Leg(sheet, CostSegment.Delivery, Side.Sell, 100_000m, Mar2).Value;

        intradayBuy.StampDuty.ShouldBe(20.00m);
        intradaySell.StampDuty.ShouldBe(0m);
        deliveryBuy.StampDuty.ShouldBe(30.00m);
        deliverySell.StampDuty.ShouldBe(0m);
        intradayBuy.DpCharge.ShouldBe(0m);
        intradaySell.DpCharge.ShouldBe(0m);
        deliveryBuy.DpCharge.ShouldBe(0m);
        deliverySell.DpCharge.ShouldBe(10m);
        intradayBuy.Stt.ShouldBe(0m, "intraday: sell side only");
        intradaySell.Stt.ShouldBe(40.00m);
        deliveryBuy.Stt.ShouldBe(80.00m, "delivery: both sides");
        deliverySell.Stt.ShouldBe(80.00m);
    }

    [Fact]
    public void Gst_is_on_the_rounded_brokerage_exchange_and_sebi_lines_and_not_on_stt_stamp_other_or_dp()
    {
        var rates = TestCostSheet.Rates(0.03m, 20m, 0m, 0.1m, 0.1m, 0.005m, 50m, 0.1m, 0.1m, 0m);
        var sheet = TestCostSheet.Of("test-gst", TestCostSheet.Schedule(TestCostSheet.ScheduleA, rates, TestCostSheet.DeliveryA));

        var leg = CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Buy, 250_250m, Mar2).Value;

        leg.Stt.ShouldBeGreaterThan(0);
        leg.StampDuty.ShouldBeGreaterThan(0);
        leg.OtherCharges.ShouldBeGreaterThan(0);
        leg.Gst.ShouldBe(Math.Round(0.18m * (leg.Brokerage + leg.ExchangeTxn + leg.SebiFee), 2, MidpointRounding.AwayFromZero), "the base is brokerage, exchange and SEBI of the leg, rounded");
    }

    [Fact]
    public void Gst_comes_from_the_rounded_lines_and_a_case_where_the_unrounded_lines_differ_by_a_paisa_is_pinned()
    {
        // turnover 8,35,000: exchange 41.75, SEBI 4.175 -> 4.18, brokerage 20; rounded base 65.93 -> GST 11.8674 -> 11.87; the unrounded base 65.925 gives 11.8665 -> 11.87 as well, so look for a turnover where they differ
        var sheet = TestCostSheet.Sheet;
        var differing = new List<decimal>();
        for (var turnover = 1_000m; turnover <= 400_000m; turnover += 137m)
        {
            var leg = CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Buy, turnover, Mar2).Value;
            var unrounded = Math.Min(20m, turnover * 0.03m / 100m) + (turnover * 0.005m / 100m) + (turnover * 50m / 10_000_000m);
            var gstOnUnrounded = Math.Round(0.18m * unrounded, 2, MidpointRounding.AwayFromZero);
            if (gstOnUnrounded != leg.Gst)
            {
                differing.Add(turnover);
            }
        }

        differing.ShouldNotBeEmpty("the grid contains turnovers where GST on the rounded lines differs from GST on the unrounded lines");
        var turnoverCase = differing[0];
        var line = CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Buy, turnoverCase, Mar2).Value;
        line.Gst.ShouldBe(Math.Round(0.18m * (line.Brokerage + line.ExchangeTxn + line.SebiFee), 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void An_exact_half_rounds_per_the_rule_of_the_schedule_and_a_double_gets_two_of_the_three_wrong()
    {
        // W1 exit SEBI 255,000 x 50 / 1,00,00,000 = 1.275; W6 stamp duty 25,025 x 0.02 % = 5.005; W7 entry SEBI 2,01,000 x 50 / 1,00,00,000 = 1.005
        var away = TestCostSheet.WithRounding(ChargeRounding.HalfAwayFromZero);
        var even = TestCostSheet.WithRounding(ChargeRounding.HalfEven);
        var down = TestCostSheet.WithRounding(ChargeRounding.Down);

        Lines(away).ShouldBe((1.28m, 5.01m, 1.01m));
        Lines(even).ShouldBe((1.28m, 5.00m, 1.00m));
        Lines(down).ShouldBe((1.27m, 5.00m, 1.00m));

        // the trap, named: the same lines in double with Math.Round(x, 2, AwayFromZero)
        Math.Round(255_000d * 50d / 10_000_000d, 2, MidpointRounding.AwayFromZero).ShouldBe(1.27, "W1's SEBI is 1.275 in decimal and a double lands under it");
        Math.Round(201_000d * 50d / 10_000_000d, 2, MidpointRounding.AwayFromZero).ShouldBe(1.00, "W7's SEBI is 1.005 in decimal and a double lands under it");
        Math.Round(25_025d * 0.02d / 100d, 2, MidpointRounding.AwayFromZero).ShouldBe(5.01, "W6's stamp duty is not a trap for a double: it is half away versus half even");

        static (decimal W1Sebi, decimal W6Stamp, decimal W7Sebi) Lines(CostSheet sheet) =>
        (
            CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Sell, 255_000m, Mar2).Value.SebiFee,
            CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Buy, 25_025m, Mar2).Value.StampDuty,
            CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Buy, 201_000m, Mar2).Value.SebiFee
        );
    }

    [Fact]
    public void The_brokerage_is_the_percent_amount_below_the_cap_the_cap_at_and_above_the_boundary_the_minimum_the_fee_and_zero()
    {
        var sheet = TestCostSheet.Sheet;
        var boundary = 20m / (0.03m / 100m);   // 66,666.666...: the turnover at which the percent amount equals the cap

        Brokerage(sheet, 50_000m).ShouldBe(15.00m, "below the boundary: the percent amount");
        Brokerage(sheet, 66_666m).ShouldBe(20.00m, "66,666 x 0.03 % = 19.9998, rounded to the paisa");
        Brokerage(sheet, Math.Ceiling(boundary)).ShouldBe(20.00m, "at the boundary: the cap");
        Brokerage(sheet, 1_000_000m).ShouldBe(20.00m, "above: the cap");
        Brokerage(sheet, boundary).ShouldBe(20.00m);

        var minimum = TestCostSheet.Of("test-min", TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.Rates(0.03m, 20m, 5m, 0m, 0m, 0m, 0m, 0m, 0m, 0m), TestCostSheet.DeliveryA));
        Brokerage(minimum, 1_000m).ShouldBe(5.00m, "a minimum above the percent amount (0.30)");
        Brokerage(minimum, 30_000m).ShouldBe(9.00m);

        var flat = TestCostSheet.Of("test-flat", TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.Rates(0m, 20m, 20m, 0m, 0m, 0m, 0m, 0m, 0m, 0m), TestCostSheet.DeliveryA));
        foreach (var turnover in new[] { 1m, 1_000m, 10_000_000m })
        {
            Brokerage(flat, turnover).ShouldBe(20.00m, "a flat fee: pct 0, min = cap = fee");
        }

        var none = TestCostSheet.Of("test-none", TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.Rates(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m), TestCostSheet.DeliveryA));
        Brokerage(none, 5_000_000m).ShouldBe(0m, "a cap of 0 is no brokerage, never an uncapped percent");

        static decimal Brokerage(CostSheet s, decimal turnover) => CostModel.Leg(s, CostSegment.IntradayEquity, Side.Buy, turnover, new DateOnly(2026, 3, 2)).Value.Brokerage;
    }

    [Fact]
    public void A_date_before_the_first_schedule_has_no_rates_and_the_error_is_not_a_zero_leg()
    {
        var leg = CostModel.Leg(TestCostSheet.Sheet, CostSegment.IntradayEquity, Side.Buy, 1_000m, new DateOnly(2025, 12, 31));

        leg.IsFailure.ShouldBeTrue();
        leg.Error.Code.ShouldBe(CostErrors.NoRatesForDateCode);
    }

    [Fact]
    public void A_negative_turnover_an_unknown_side_and_a_null_sheet_are_caller_errors()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CostModel.Leg(TestCostSheet.Sheet, CostSegment.IntradayEquity, Side.Buy, -1m, Mar2));
        Should.Throw<ArgumentOutOfRangeException>(() => CostModel.Leg(TestCostSheet.Sheet, CostSegment.IntradayEquity, (Side)9, 1m, Mar2));
        Should.Throw<ArgumentNullException>(() => CostModel.Leg(null!, CostSegment.IntradayEquity, Side.Buy, 1m, Mar2));
        Should.Throw<ArgumentOutOfRangeException>(() => CostModel.Leg(TestCostSheet.Sheet, (CostSegment)9, Side.Buy, 1m, Mar2));
    }

    [Fact]
    public void A_zero_turnover_charges_nothing_but_a_minimum_brokerage()
    {
        CostModel.Leg(TestCostSheet.Sheet, CostSegment.IntradayEquity, Side.Buy, 0m, Mar2).Value.Total.ShouldBe(0m);
    }

    public static IEnumerable<(CostSegment Segment, Side Side, decimal Turnover, DateOnly Date)> SeededLegs()
    {
        var random = new Random(20261019);
        for (var i = 0; i < 2_000; i++)
        {
            yield return (
                random.Next(2) == 0 ? CostSegment.IntradayEquity : CostSegment.Delivery,
                random.Next(2) == 0 ? Side.Buy : Side.Sell,
                Math.Round((decimal)(random.NextDouble() * random.NextDouble() * 5_000_000), 2),
                random.Next(2) == 0 ? Mar2 : new DateOnly(2026, 3, 4));
        }
    }

    [Fact]
    public void Every_line_and_the_total_are_not_negative_the_total_never_falls_as_the_turnover_grows_and_the_cap_does_not_double()
    {
        var sheet = TestCostSheet.Sheet;
        foreach (var segment in Enum.GetValues<CostSegment>())
        {
            foreach (var side in Enum.GetValues<Side>())
            {
                foreach (var date in new[] { Mar2, Mar3 })
                {
                    var previous = -1m;
                    for (var turnover = 0m; turnover <= 3_000_000m; turnover += 997m)
                    {
                        var leg = CostModel.Leg(sheet, segment, side, turnover, date).Value;
                        AllLinesAreNotNegative(leg);
                        leg.Total.ShouldBeGreaterThanOrEqualTo(previous, $"{segment} {side} {date} at {turnover}");
                        previous = leg.Total;
                    }
                }
            }
        }

        foreach (var (segment, side, turnover, date) in SeededLegs())
        {
            var one = CostModel.Leg(sheet, segment, side, turnover, date).Value;
            var two = CostModel.Leg(sheet, segment, side, 2 * turnover, date).Value;
            AllLinesAreNotNegative(one);
            two.Total.ShouldBeLessThanOrEqualTo((2 * one.Total) + 0.10m, $"{segment} {side} {turnover}");
            CostModel.Leg(sheet, segment, side, turnover + 0.01m, date).Value.Total.ShouldBeGreaterThanOrEqualTo(one.Total);
        }
    }

    [Fact]
    public void A_sheet_with_every_rate_zero_charges_nothing_and_a_line_set_to_zero_is_zero_in_the_leg()
    {
        var zero = TestCostSheet.Rates(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);
        var allZero = TestCostSheet.Of("test-zero", TestCostSheet.Schedule(TestCostSheet.ScheduleA, zero, zero, gst: 0m));
        var gstOnly = TestCostSheet.Of("test-gst-zero", TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.IntradayA, TestCostSheet.DeliveryA, gst: 0m));

        foreach (var (segment, side, turnover, date) in SeededLegs().Take(200))
        {
            CostModel.Leg(allZero, segment, side, turnover, date).Value.Total.ShouldBe(0m);
            CostModel.Leg(gstOnly, segment, side, turnover, date).Value.Gst.ShouldBe(0m);
            CostModel.Leg(TestCostSheet.Sheet, segment, side, turnover, date).Value.OtherCharges.ShouldBe(0m, "the sheet's other_pct is 0");
        }
    }

    [Fact]
    public void The_production_model_agrees_with_the_independent_reference_on_2000_seeded_legs_and_all_three_roundings()
    {
        foreach (var rounding in Enum.GetValues<ChargeRounding>())
        {
            var sheet = TestCostSheet.WithRounding(rounding);
            foreach (var (segment, side, turnover, date) in SeededLegs())
            {
                CostModel.Leg(sheet, segment, side, turnover, date).Value.ShouldBe(ReferenceCosting.Leg(sheet, segment, side, turnover, date), $"{rounding} {segment} {side} {turnover}");
            }
        }
    }

    [Fact]
    public void An_every_line_sheet_charges_a_non_zero_amount_in_every_line_it_applies_to()
    {
        var rates = TestCostSheet.Rates(0.03m, 20m, 1m, 0.1m, 0.1m, 0.005m, 50m, 0.02m, 0.01m, 10m);
        var sheet = TestCostSheet.Of("test-all", TestCostSheet.Schedule(TestCostSheet.ScheduleA, rates, rates));

        var sell = CostModel.Leg(sheet, CostSegment.Delivery, Side.Sell, 500_000m, Mar2).Value;
        var buy = CostModel.Leg(sheet, CostSegment.Delivery, Side.Buy, 500_000m, Mar2).Value;

        new[] { sell.Brokerage, sell.Stt, sell.ExchangeTxn, sell.SebiFee, sell.OtherCharges, sell.DpCharge, sell.Gst }.ShouldAllBe(v => v > 0);
        sell.StampDuty.ShouldBe(0m);
        new[] { buy.Brokerage, buy.Stt, buy.ExchangeTxn, buy.SebiFee, buy.StampDuty, buy.OtherCharges, buy.Gst }.ShouldAllBe(v => v > 0);
        buy.DpCharge.ShouldBe(0m);
    }

    private static void AllLinesAreNotNegative(LegCharges leg) =>
        new[] { leg.Brokerage, leg.Stt, leg.ExchangeTxn, leg.SebiFee, leg.StampDuty, leg.OtherCharges, leg.DpCharge, leg.Gst, leg.Total }.ShouldAllBe(v => v >= 0);
}
