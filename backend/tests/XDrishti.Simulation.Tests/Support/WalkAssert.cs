using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>Checks one walk against what the spec says it gives, field by field.</summary>
internal static class WalkAssert
{
    public static Result<WalkOutcome> Walk(WalkCase walkCase, WalkItem item) => TradeWalker.Walk(item.Candidate, walkCase.Bars, walkCase.Rules);

    public static void Matches(WalkCase walkCase, WalkItem item, Result<WalkOutcome> result)
    {
        result.IsSuccess.ShouldBeTrue(walkCase.Name + ": " + result.Error.Code);
        var outcome = result.Value;
        var label = walkCase.Name + " " + item.Candidate.Side;

        outcome.BarsExamined.ShouldBe(item.Minutes, label + " minutes examined");
        if (item.Trade is not { } expected)
        {
            outcome.Trade.ShouldBeNull(label);
            outcome.NoTrade.ShouldBe(item.NoTrade, label);
            outcome.EntryAfterMissingMinutes.ShouldBeFalse(label);
            outcome.StopGapExit.ShouldBeFalse(label);
            outcome.TargetGapExit.ShouldBeFalse(label);
            outcome.AmbiguousMinute.ShouldBeFalse(label);
            outcome.LateSquareOff.ShouldBeFalse(label);
            outcome.ZeroVolumeMinutes.ShouldBe(0, label);
            return;
        }

        outcome.NoTrade.ShouldBeNull(label);
        var trade = outcome.Trade.ShouldNotBeNull(label);
        var date = DayBuilder.Monday;
        var candidate = item.Candidate;

        trade.SchemaVersion.ShouldBe(SimTrade.CurrentSchemaVersion, label);
        trade.StrategyId.ShouldBe(candidate.StrategyId, label);
        trade.InstrumentId.ShouldBe(candidate.Instrument.Id, label);
        trade.Symbol.ShouldBe("TEST", label);
        trade.Side.ShouldBe(candidate.Side, label);
        trade.TradeDate.ShouldBe(date, label);
        trade.DecisionEpochSeconds.ShouldBe(candidate.DecisionEpochSeconds, label);
        trade.TriggerTimeframe.ShouldBe(candidate.TriggerTimeframe, label);
        trade.TriggerBar.ShouldBe(candidate.TriggerBar, label);
        trade.TriggerBarMinutes.ShouldBe(candidate.TriggerBarMinutes, label);
        trade.TriggerBarComplete.ShouldBe(candidate.TriggerBarComplete, label);
        trade.EntryEpochSeconds.ShouldBe(DayBuilder.Epoch(date, expected.EntryTime), label + " entry instant");
        trade.EntryPrice.ShouldBe(expected.Entry, label + " entry price");
        trade.StopPrice.ShouldBe(expected.Stop, label + " stop");
        trade.TargetPrice.ShouldBe(expected.Target, label + " target");
        trade.ExitEpochSeconds.ShouldBe(DayBuilder.Epoch(date, expected.ExitTime), label + " exit instant");
        trade.ExitPrice.ShouldBe(expected.Exit, label + " exit price");
        trade.ExitReason.ShouldBe(expected.Reason, label);
        trade.RExact.ShouldBe(expected.R, label + " R");
        trade.MaeR.ShouldBe(expected.Mae, label + " MAE");
        trade.MfeR.ShouldBe(expected.Mfe, label + " MFE");
        trade.Fills.ShouldBe(FillBasis.Exact, label);
        trade.Costs.ShouldBe(CostBasis.None, label);
        trade.EntrySnapshotJson.ShouldBeNull(label);

        // The compatibility rules (no slippage) and a limit that does not bind: the filled layer is the exact one and every entry is at the open.
        trade.EntryFilledPrice.ShouldBe(trade.EntryPrice, label);
        trade.ExitFilledPrice.ShouldBe(trade.ExitPrice, label);
        trade.RFilled.ShouldBe(trade.RExact, label);
        trade.SlippageR.ShouldBe(0, label);
        trade.EntryFill.ShouldBe(EntryFill.AtOpen, label);
        trade.TickFallback.ShouldBeFalse(label);
        outcome.AmbiguousMinute.ShouldBe(expected.Ambiguous, label + " ambiguous");
        outcome.LateSquareOff.ShouldBe(expected.Late, label + " late square-off");
        outcome.RiskUnderFourTicks.ShouldBeFalse(label);
        outcome.ZeroVolumeMinutes.ShouldBe(0, label);
        outcome.EntryAfterMissingMinutes.ShouldBe(expected.Delayed, label + " entry after missing minutes");
        outcome.StopGapExit.ShouldBe(expected.StopGap, label + " stop gap");
        outcome.TargetGapExit.ShouldBe(expected.TargetGap, label + " target gap");
    }
}
