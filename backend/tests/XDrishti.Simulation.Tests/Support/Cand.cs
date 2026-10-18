using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>The hand-made entry candidates of the fixtures (D9): the walker needs no scanner, so a test writes the decision down.</summary>
internal static class Cand
{
    /// <summary>The one invented instrument of the fixtures.</summary>
    public static readonly InstrumentRef Test = new(new Guid("00000000-0000-0000-0000-000000000101"), "TEST");

    /// <summary>The NSE equity tick, 0.05 rupees, as the walker takes it.</summary>
    public static readonly TickSpec Tick05 = TickSpec.FromRupees(0.05m).Value;

    /// <summary>The fill rules of the compatibility with P3-03 (criterion 17): the 0.1 % entry limit and no slippage, so that the fills are the exact prices of the table.</summary>
    public static readonly FillRules NoSlippage = FillRules.Create(0.1, SlippageConfig.None).Value;

    /// <summary>The rules of the fixtures unless a row says otherwise: reward-risk 2.0, square-off at 15:15, the 0.1 % entry limit, no slippage, tick 0.05 (the compatibility rules of criterion 17).</summary>
    public static readonly WalkRules Standard = new(2.0, 915, NoSlippage, Tick05);

    /// <summary>The same exit rules with the doc 16 defaults for slippage: what a run uses.</summary>
    public static readonly WalkRules Modelled = Standard with { Fills = FillRules.Default };

    /// <summary>A candidate on the date <paramref name="date"/>: the decision is at <paramref name="decision"/> (HH:MM), the end of the minute <c>bars[index]</c>.</summary>
    public static EntryCandidate Make(
        Side side, int index, string decision, double stop, string triggerStart, (double Open, double High, double Low, double Close) trigger, int minutes = 15, bool complete = true, long? volume = null, DateOnly? date = null)
    {
        var day = date ?? DayBuilder.Monday;
        var bar = new Bar(DayBuilder.Epoch(day, triggerStart), trigger.Open, trigger.High, trigger.Low, trigger.Close, volume ?? 1_000L * minutes);
        return new EntryCandidate("ORB", Test, side, index, DayBuilder.Epoch(day, decision), Timeframe.M15, bar, minutes, complete, stop, DecisionTrace.Empty);
    }

    /// <summary>Cand-B: BUY, index 29, decision 09:45, stop 98.00, trigger bar 09:30 (O 99.00 H 100.50 L 99.00 C 100.50, 15 minutes, complete).</summary>
    public static EntryCandidate B() => Make(Side.Buy, 29, "09:45", 98.00, "09:30", (99.00, 100.50, 99.00, 100.50));

    /// <summary>The same candidate with another reward-risk is the same candidate: the seam of AC6 (the candidate holds no exit).</summary>
    public static EntryCandidate Buy(int index, string decision, double stop = 98.00, string triggerStart = "09:30") =>
        Make(Side.Buy, index, decision, stop, triggerStart, (99.00, 100.50, 99.00, 100.50));

    public static EntryCandidate Sell(int index, string decision, double stop, string triggerStart, (double Open, double High, double Low, double Close) trigger) =>
        Make(Side.Sell, index, decision, stop, triggerStart, trigger);
}
