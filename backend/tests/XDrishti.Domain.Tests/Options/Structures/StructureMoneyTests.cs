using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Research;
using XDrishti.Domain.Tests.Options.Remark;

namespace XDrishti.Domain.Tests.Options.Structures;

/// <summary>P14-10 criterion 1 (units) and the net rule: the units buy exactly the maximum loss; a leg with no spread or no option lines is never costed as zero; eight fills cost eight half-spreads.</summary>
public sealed class StructureMoneyTests
{
    private static readonly DateOnly Day = new(2025, 6, 10);

    [Fact]
    public void The_units_are_the_risk_over_the_maximum_loss_in_rupees_and_a_non_positive_input_gives_none()
    {
        RemarkMoney.UnitsForMaxLoss(1000m, 6000).ShouldBe(1000m / 60m);
        RemarkMoney.UnitsForMaxLoss(0m, 6000).ShouldBeNull();
        RemarkMoney.UnitsForMaxLoss(1000m, 0).ShouldBeNull();
        RemarkMoney.UnitsForMaxLoss(-5m, 6000).ShouldBeNull();
    }

    [Fact]
    public void Four_fills_cost_four_half_spreads_and_eight_cost_eight()
    {
        var spread = new HalfSpreadValue(HalfSpreadSource.Measured, 1m, "spread: measured (60 sessions)", null, false);
        var fill = new RemarkMoney.LegFill(Side.Buy, 10_000, Day, spread);

        var four = RemarkMoney.NetLegs(RemarkKit.OptionSheet, 0m, 10m, [fill, fill, fill, fill]);
        var eight = RemarkMoney.NetLegs(RemarkKit.OptionSheet, 0m, 10m, [.. Enumerable.Repeat(fill, 8)]);

        four.Fate.ShouldBeNull();
        eight.Fate.ShouldBeNull();
        eight.NetInr!.Value.ShouldBe(four.NetInr!.Value * 2, 0.02m);
        four.NetInr!.Value.ShouldBeLessThan(0m);
    }

    [Fact]
    public void An_unavailable_half_spread_on_any_fill_is_spread_unavailable_and_never_zero()
    {
        var good = new RemarkMoney.LegFill(Side.Buy, 10_000, Day, new HalfSpreadValue(HalfSpreadSource.Measured, 1m, "spread: measured", null, false));
        var bad = new RemarkMoney.LegFill(Side.Sell, 10_000, Day, HalfSpreadValue.Unavailable);

        var outcome = RemarkMoney.NetLegs(RemarkKit.OptionSheet, 100m, 10m, [good, bad]);

        outcome.NetInr.ShouldBeNull();
        outcome.Fate.ShouldBe(RemarkFates.SpreadUnavailable);
    }
}
