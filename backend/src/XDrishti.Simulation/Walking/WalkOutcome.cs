using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// What one walk gave: a trade, or the reason there is none. The flags are the facts that <see cref="WalkCounters"/> sums (ADR 0037, decision D6; ADR 0038; ADR 0040, D10). Exactly one of <paramref name="Trade"/> and <paramref name="NoTrade"/> is set.
/// </summary>
/// <param name="Trade">The trade, or null when there is none.</param>
/// <param name="NoTrade">The reason there is no trade, or null when there is one.</param>
/// <param name="BarsExamined">The minutes that hold a trade, from the fill minute to the exit minute, both included: 0 for <c>NoFillBar</c>, <c>AfterSquareOff</c>, <c>PositionOpen</c> and <c>OrderPendingAtEnd</c>, 1 for <c>EntryThroughStop</c> and <c>LimitNotReached</c>.</param>
/// <param name="EntryAfterMissingMinutes">The trade was filled in a minute that starts later than the decision instant; false when there is no trade.</param>
/// <param name="StopGapExit">The trade left at a stop (or at the open beyond it) that its minute had opened strictly beyond.</param>
/// <param name="TargetGapExit">The trade left at a target that its minute had opened strictly beyond.</param>
/// <param name="AmbiguousMinute">The exit minute reached both the stop and the target with its open strictly between them, and was taken as a stop.</param>
/// <param name="LateSquareOff">The trade left at the square-off at a minute after the square-off minute.</param>
/// <param name="RiskUnderFourTicks">The risk of the trade is under four ticks.</param>
/// <param name="ZeroVolumeMinutes">Minutes with volume 0 passed over between the decision instant and the exit (before the deciding minute, for a no-trade).</param>
/// <param name="MaxHoldAtLastClose">The trade left with <c>MaxHold</c> at the close of the last minute that holds a trade because its due minute held none (ADR 0040, X8).</param>
/// <param name="OvernightStopGap">A stop gap exit whose minute is the first that holds a trade on a date later than the entry date.</param>
/// <param name="OvernightTargetGap">A target gap exit whose minute is the first that holds a trade on a date later than the entry date.</param>
/// <param name="OvernightEntry">The trade was filled on a later date than the one on which it was decided: an order that rested overnight (ADR 0043, E8 and E9).</param>
/// <param name="LegsTotal">The legs of the trade when it has legs (P4-01); 0 otherwise.</param>
/// <param name="StopLoosenIgnored">The requests of an exit rule to loosen the stop that the walker ignored (P4-01, D6).</param>
/// <param name="HoldEndEpochSeconds">The instant the position counts as open until, for the position gate (D11): the exit instant, or the end of the due session for a <c>MaxHoldAtLastClose</c> trade; 0 when there is no trade.</param>
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
    int ZeroVolumeMinutes,
    bool MaxHoldAtLastClose = false,
    bool OvernightStopGap = false,
    bool OvernightTargetGap = false,
    long HoldEndEpochSeconds = 0,
    bool OvernightEntry = false,
    int LegsTotal = 0,
    int StopLoosenIgnored = 0)
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
            RiskUnderFourTicks: RiskUnderFourTicks ? 1 : 0,
            PositionOpen: NoTrade == NoTradeReason.PositionOpen ? 1 : 0,
            ExitsMaxHold: exit == ExitReason.MaxHold ? 1 : 0,
            MaxHoldAtLastClose: MaxHoldAtLastClose ? 1 : 0,
            OvernightStopGaps: OvernightStopGap ? 1 : 0,
            OvernightTargetGaps: OvernightTargetGap ? 1 : 0,
            SessionsHeld: Trade?.SessionsHeld ?? 0,
            SessionsUnseen: Trade?.SessionsUnseen ?? 0,
            OvernightEntries: OvernightEntry ? 1 : 0,
            OrderPendingAtEnd: NoTrade == NoTradeReason.OrderPendingAtEnd ? 1 : 0,
            LegsTotal: LegsTotal,
            StopLoosenIgnored: StopLoosenIgnored,
            ExitsEmaExit: exit == ExitReason.EmaExit ? 1 : 0);
    }
}
