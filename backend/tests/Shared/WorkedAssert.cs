using XDrishti.Domain.Research;

namespace XDrishti.Tests.Shared;

/// <summary>The assertions of a costed trade against the hand figures of <see cref="WorkedCosts"/>, written once for the tests of the applier and of the replay: money to the paisa, ratios to 1e-9.</summary>
internal static class WorkedAssert
{
    /// <summary>The costing of the trade equals the hand-worked trade in every field, and the trade says it is charged.</summary>
    public static void ShouldMatch(SimTrade kept, WorkedCosts.HandTrade hand, CostSegment segment = CostSegment.IntradayEquity, string sheetId = TestCostSheet.Id)
    {
        var costing = kept.Costing.ShouldNotBeNull(hand.Name);
        kept.Costs.ShouldBe(CostBasis.Charged, hand.Name);
        costing.Segment.ShouldBe(segment, hand.Name);
        costing.SheetId.ShouldBe(sheetId, hand.Name);
        costing.Quantity.ShouldBe(hand.Quantity, hand.Name);
        costing.RiskTaken.ShouldBe(hand.RiskTaken, hand.Name);
        costing.EntryTurnover.ShouldBe(hand.EntryTurnover, hand.Name);
        costing.ExitTurnover.ShouldBe(hand.ExitTurnover, hand.Name);
        costing.EntryLeg.ShouldBe(hand.Entry.Charges, hand.Name + " entry leg");
        costing.ExitLeg.ShouldBe(hand.Exit.Charges, hand.Name + " exit leg");
        costing.EntryLeg.Total.ShouldBe(hand.Entry.Total, hand.Name);
        costing.ExitLeg.Total.ShouldBe(hand.Exit.Total, hand.Name);
        costing.TotalCost.ShouldBe(hand.Total, hand.Name);
        costing.CostR.ShouldBe((double)hand.CostR, 1e-9, hand.Name);
        costing.RNet.ShouldBe(hand.RNet, 1e-9, hand.Name);
        costing.RNet.ShouldBe(kept.RFilled - costing.CostR, hand.Name + ": net R is the filled R less the charge in R");
    }
}
