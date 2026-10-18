using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// What one walk gave: a trade, or the reason there is none. The flags are the facts that <see cref="WalkCounters"/> sums (ADR 0037, decision D6; ADR 0038). Exactly one of <paramref name="Trade"/> and <paramref name="NoTrade"/> is set.
/// </summary>
/// <param name="Trade">The trade, or null when there is none.</param>
/// <param name="NoTrade">The reason there is no trade, or null when there is one.</param>
/// <param name="BarsExamined">The minutes that hold a trade, from the fill minute to the exit minute, both included: 0 for <c>NoFillBar</c> and <c>AfterSquareOff</c>, 1 for <c>EntryThroughStop</c> and <c>LimitNotReached</c>.</param>
/// <param name="EntryAfterMissingMinutes">The trade was filled in a minute that starts later than the decision instant; false when there is no trade.</param>
/// <param name="StopGapExit">The trade left at a stop (or at the open beyond it) that its minute had opened strictly beyond.</param>
/// <param name="TargetGapExit">The trade left at a target that its minute had opened strictly beyond.</param>
/// <param name="AmbiguousMinute">The exit minute reached both the stop and the target with its open strictly between them, and was taken as a stop.</param>
/// <param name="LateSquareOff">The trade left at the square-off at a minute after the square-off minute.</param>
/// <param name="RiskUnderFourTicks">The risk of the trade is under four ticks.</param>
/// <param name="ZeroVolumeMinutes">Minutes with volume 0 passed over between the decision instant and the exit (before the deciding minute, for a no-trade).</param>
public readonly record struct WalkOutcome(
    SimTrade? Trade,
    NoTradeReason? NoTrade,
    int BarsExamined,
    bool EntryAfterMissingMinutes,
    bool StopGapExit,
    bool TargetGapExit,
    bool AmbiguousMinute,
    bool LateSquareOff,
    bool RiskUnderFourTicks,
    int ZeroVolumeMinutes)
{
    /// <summary>The counts of this one walk, to be summed with <see cref="WalkCounters.Add"/>.</summary>
    public WalkCounters ToCounters()
    {
        var exit = Trade?.ExitReason;
        var entry = Trade?.EntryFill;
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
            BarsExamined: BarsExamined,
            LimitNotReached: NoTrade == NoTradeReason.LimitNotReached ? 1 : 0,
            EntriesAtOpen: entry == EntryFill.AtOpen ? 1 : 0,
            EntriesAtLimit: entry == EntryFill.AtLimit ? 1 : 0,
            AmbiguousMinutes: AmbiguousMinute ? 1 : 0,
            LateSquareOffs: LateSquareOff ? 1 : 0,
            ZeroVolumeMinutes: ZeroVolumeMinutes,
            RiskUnderFourTicks: RiskUnderFourTicks ? 1 : 0);
    }
}
