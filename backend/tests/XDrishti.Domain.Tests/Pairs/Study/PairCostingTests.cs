using XDrishti.Domain.PairResearch;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Pairs.Study;

/// <summary>P14-13: four fills through the cost model (intraday equity) when the owner's sheet exists, gross and labelled when not, never zero charges when the sheet cannot charge.</summary>
public sealed class PairCostingTests
{
    private static readonly long Entry = new DateTimeOffset(2026, 6, 8, 11, 0, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();

    private static PairTrade Trade(PairSide side = PairSide.ShortSpread) =>
        new("NSE:Y|NSE:X", side, Entry, Entry + 600, 2.2, 0.36, "exit", 991, 1000, 743.25m, null, 600m, 743.25 / 600.0, 0m, 100.90m, 100.15m, 100.00m, 100.00m, ["leg_late"]);

    private static decimal Leg(CostSheet sheet, Side side, decimal turnover) => CostModel.Leg(sheet, CostSegment.IntradayEquity, side, turnover, new DateOnly(2026, 6, 8)).Value.Total;

    [Fact]
    public void With_a_sheet_the_four_legs_are_charged_and_net_is_gross_less_the_charges_and_R_is_net_over_the_risk()
    {
        var sheet = TestCostSheet.Sheet;

        var costed = PairCosting.Apply(Trade(), sheet, out var unavailable);

        var charges = Leg(sheet, Side.Sell, 100.90m * 991) + Leg(sheet, Side.Buy, 100.15m * 991) + Leg(sheet, Side.Buy, 100m * 1000) + Leg(sheet, Side.Sell, 100m * 1000);
        unavailable.ShouldBeFalse();
        costed.NetInr.ShouldBe(743.25m - charges);
        costed.R.ShouldBe((double)((743.25m - charges) / 600m), 1e-12);
        costed.Labels.ShouldBe(["leg_late"]);
        costed.GrossInr.ShouldBe(743.25m);
    }

    [Fact]
    public void A_long_spread_buys_y_first_and_sells_x_first()
    {
        var sheet = TestCostSheet.Sheet;

        var costed = PairCosting.Apply(Trade(PairSide.LongSpread), sheet, out _);

        var charges = Leg(sheet, Side.Buy, 100.90m * 991) + Leg(sheet, Side.Sell, 100.15m * 991) + Leg(sheet, Side.Sell, 100m * 1000) + Leg(sheet, Side.Buy, 100m * 1000);
        costed.NetInr.ShouldBe(743.25m - charges);
    }

    [Fact]
    public void Without_a_sheet_the_trade_stays_gross_and_says_so()
    {
        var costed = PairCosting.Apply(Trade(), null, out var unavailable);

        unavailable.ShouldBeFalse();
        costed.NetInr.ShouldBeNull();
        costed.Labels.ShouldBe(["leg_late", "gross"]);
        costed.R.ShouldBe(743.25 / 600.0, 1e-12);
    }

    [Fact]
    public void A_date_before_the_first_schedule_leaves_the_net_unavailable_never_zero_charges()
    {
        var late = TestCostSheet.Of("test-late", TestCostSheet.Schedule(new DateOnly(2026, 12, 1), TestCostSheet.IntradayA, TestCostSheet.DeliveryA));

        var costed = PairCosting.Apply(Trade(), late, out var unavailable);

        unavailable.ShouldBeTrue();
        costed.NetInr.ShouldBeNull();
        costed.Labels.ShouldBe(["leg_late", "costs_unavailable"]);
        costed.R.ShouldBe(743.25 / 600.0, 1e-12);
    }

    [Fact]
    public void Costing_twice_does_not_stack_labels()
    {
        var once = PairCosting.Apply(Trade(), null, out _);

        var twice = PairCosting.Apply(once, null, out _);

        twice.Labels.Count(l => l == "gross").ShouldBe(1);
    }
}
