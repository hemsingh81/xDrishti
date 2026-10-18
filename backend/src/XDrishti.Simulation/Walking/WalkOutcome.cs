using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// What one walk gave: a trade, or the reason there is none. The flags are the facts that <see cref="WalkCounters"/> sums (ADR 0037, decision D6). Exactly one of <paramref name="Trade"/> and <paramref name="NoTrade"/> is set.
/// </summary>
/// <param name="Trade">The trade, or null when there is none.</param>
/// <param name="NoTrade">The reason there is no trade, or null when there is one.</param>
/// <param name="BarsExamined">The listed minutes the walk looked at, from the fill minute to the exit minute, both included: 0 for <c>NoFillBar</c> and <c>AfterSquareOff</c>, 1 for <c>EntryThroughStop</c>.</param>
/// <param name="EntryAfterMissingMinutes">The trade was filled in a minute that starts later than the decision instant; false when there is no trade.</param>
/// <param name="StopGapExit">The trade left at a stop that its minute had opened beyond.</param>
/// <param name="TargetGapExit">The trade left at a target that its minute had opened beyond.</param>
public readonly record struct WalkOutcome(SimTrade? Trade, NoTradeReason? NoTrade, int BarsExamined, bool EntryAfterMissingMinutes, bool StopGapExit, bool TargetGapExit)
{
    /// <summary>The counts of this one walk, to be summed with <see cref="WalkCounters.Add"/>.</summary>
    public WalkCounters ToCounters()
    {
        var exit = Trade?.ExitReason;
        return new WalkCounters(
            Candidates: 1,
            Trades: Trade is null ? 0 : 1,
            NoFillBar: NoTrade == NoTradeReason.NoFillBar ? 1 : 0,
            AfterSquareOff: NoTrade == NoTradeReason.AfterSquareOff ? 1 : 0,
            EntryThroughStop: NoTrade == NoTradeReason.EntryThroughStop ? 1 : 0,
            ExitsStop: exit == ExitReason.Stop ? 1 : 0,
            ExitsTarget: exit == ExitReason.Target ? 1 : 0,
            ExitsSquareOff: exit == ExitReason.SquareOff ? 1 : 0,
            ExitsEndOfData: exit == ExitReason.EndOfData ? 1 : 0,
            StopGapExits: StopGapExit ? 1 : 0,
            TargetGapExits: TargetGapExit ? 1 : 0,
            EntriesAfterMissingMinutes: EntryAfterMissingMinutes ? 1 : 0,
            BarsExamined: BarsExamined);
    }
}
