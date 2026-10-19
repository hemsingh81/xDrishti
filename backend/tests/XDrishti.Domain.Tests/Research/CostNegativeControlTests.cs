using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// Criterion 23 of P3-03c, the negative controls: a check that passes whatever the code does proves nothing, so each of five mutants written in this project (stamp duty on sells too; GST computed on STT as well; the brokerage cap ignored;
/// a rounded quantity in place of a cut one; the total rounded once instead of each line) must fail the worked examples, while the production code and an unbroken reference must pass them. Three more mutants (the DP charge on buys, GST on every
/// line, the STT on the wrong side) go beyond the five.
/// </summary>
public sealed class CostNegativeControlTests
{
    private static readonly SizingProfile Standard = SizingProfile.Standard;

    private static readonly decimal[] Turnovers = [10_000m, 250_000m, 1_000_000m];

    /// <summary>The worked legs W1 to W8 and L1 and the quantities of W1, W2, W4 and W7 hold under these functions.</summary>
    private static bool WorkedFiguresHold(Func<CostSegment, Side, decimal, DateOnly, LegCharges> leg, Func<decimal, decimal, decimal, long> quantity, Func<LegCharges, decimal> total)
    {
        foreach (var worked in WorkedCosts.Legs)
        {
            var lines = leg(worked.Segment, worked.Side, worked.Turnover, worked.Date);
            if (lines != worked.Expected.Charges || total(lines) != worked.Expected.Total)
            {
                return false;
            }
        }

        return quantity(1_000_000m, 100.00m, 1.00m) == 2_500
            && quantity(1_000_000m, 199.80m, 2.20m) == 1_251
            && quantity(10_000m, 3_000.00m, 30.00m) == 0
            && quantity(1_000_000m, 100.50m, 2.50m) == 2_000;
    }

    private static bool ProductionHolds() => WorkedFiguresHold(
        (segment, side, turnover, date) => CostModel.Leg(TestCostSheet.Sheet, segment, side, turnover, date).Value,
        (capital, entry, risk) => Sizing.Quantity(capital, Standard, entry, risk).Value,
        leg => leg.Total);

    private static bool MutantHolds(ReferenceCosting.Break mutant) => WorkedFiguresHold(
        (segment, side, turnover, date) => ReferenceCosting.Leg(TestCostSheet.Sheet, segment, side, turnover, date, mutant),
        (capital, entry, risk) => ReferenceCosting.Quantity(capital, Standard, entry, risk, mutant),
        leg => ReferenceCosting.Total(leg, mutant));

    [Fact]
    public void The_production_code_and_the_unbroken_reference_pass_the_worked_figures()
    {
        ProductionHolds().ShouldBeTrue();
        MutantHolds(ReferenceCosting.Break.None).ShouldBeTrue("the harness can pass: the reference is the spec's table");
    }

    [Theory]
    [InlineData(1)] // StampOnSells
    [InlineData(2)] // GstOnStt
    [InlineData(4)] // IgnoreBrokerageCap
    [InlineData(16)] // RoundQuantity
    [InlineData(8)] // TotalRoundedOnce
    [InlineData(32)] // DpOnBuys
    [InlineData(64)] // GstOnAllLines
    [InlineData(128)] // SttOnWrongSide
    public void Each_mutant_fails_at_least_one_worked_figure(int mutantBits)
    {
        var mutant = (ReferenceCosting.Break)mutantBits;

        MutantHolds(mutant).ShouldBeFalse(mutant + " should be caught by the worked examples");
    }

    [Fact]
    public void The_worked_examples_that_catch_each_of_the_five_mutants_are_the_ones_the_spec_names()
    {
        // stamp duty on sells: W1's exit leg would gain 51 rupees; GST on STT: W1's exit GST would be 24.49 not 6.13; the cap ignored: W1's brokerage 75.08 not 20.00; the total rounded once: W1's exchange line 12.5125 not 12.51
        var stampOnSell = ReferenceCosting.Leg(TestCostSheet.Sheet, CostSegment.IntradayEquity, Side.Sell, 255_000m, new DateOnly(2026, 3, 2), ReferenceCosting.Break.StampOnSells);
        var gstOnStt = ReferenceCosting.Leg(TestCostSheet.Sheet, CostSegment.IntradayEquity, Side.Sell, 255_000m, new DateOnly(2026, 3, 2), ReferenceCosting.Break.GstOnStt);
        var noCap = ReferenceCosting.Leg(TestCostSheet.Sheet, CostSegment.IntradayEquity, Side.Buy, 250_250m, new DateOnly(2026, 3, 2), ReferenceCosting.Break.IgnoreBrokerageCap);
        var unrounded = ReferenceCosting.Leg(TestCostSheet.Sheet, CostSegment.IntradayEquity, Side.Buy, 250_250m, new DateOnly(2026, 3, 2), ReferenceCosting.Break.TotalRoundedOnce);

        stampOnSell.StampDuty.ShouldBe(51.00m);
        gstOnStt.Gst.ShouldBe(Math.Round(0.18m * (20.00m + 102.00m + 12.75m + 1.28m), 2, MidpointRounding.AwayFromZero));
        gstOnStt.Gst.ShouldNotBe(WorkedCosts.W1Exit.Gst);
        noCap.Brokerage.ShouldBe(75.08m);
        unrounded.ExchangeTxn.ShouldBe(12.5125m);
        ReferenceCosting.Quantity(10_000m, Standard, 3_000m, 30m, ReferenceCosting.Break.RoundQuantity).ShouldBe(1, "W4 would not be skipped");
    }

    [Fact]
    public void The_properties_also_catch_the_mutants_that_the_worked_examples_catch()
    {
        // a property that needs no hand figure: with the cap ignored the leg total is not bounded by twice itself at 2T (the percent brokerage doubles but the cap should not) -- here the stamp mutant breaks "no stamp duty on a sell"
        var sheet = TestCostSheet.Sheet;
        var sells = Turnovers.Select(t => ReferenceCosting.Leg(sheet, CostSegment.IntradayEquity, Side.Sell, t, new DateOnly(2026, 3, 2), ReferenceCosting.Break.StampOnSells));
        var honest = Turnovers.Select(t => CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Sell, t, new DateOnly(2026, 3, 2)).Value);

        sells.Any(l => l.StampDuty != 0m).ShouldBeTrue();
        honest.ShouldAllBe(l => l.StampDuty == 0m);
        var capped = ReferenceCosting.Leg(sheet, CostSegment.IntradayEquity, Side.Buy, 1_000_000m, new DateOnly(2026, 3, 2), ReferenceCosting.Break.IgnoreBrokerageCap);
        capped.Brokerage.ShouldBeGreaterThan(20m);
        CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Buy, 1_000_000m, new DateOnly(2026, 3, 2)).Value.Brokerage.ShouldBeLessThanOrEqualTo(20m);
    }
}
