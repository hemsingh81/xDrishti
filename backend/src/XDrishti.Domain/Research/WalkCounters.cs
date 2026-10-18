namespace XDrishti.Domain.Research;

/// <summary>
/// What the trade walker did, as plain sums (ADR 0037, decision D6; ADR 0038). A rule that never fires gives zero trades and zero losses and looks safe; these counters are how it is seen.
/// Identities: <c>Candidates = Trades + NoFillBar + AfterSquareOff + EntryThroughStop + LimitNotReached</c>, <c>Trades = EntriesAtOpen + EntriesAtLimit</c>, and <c>Trades</c> is the sum of the four exit counts.
/// <c>WalkOutcome.ToCounters()</c> (in <c>XDrishti.Simulation.Walking</c>) makes the counts of one walk; this type lives in Domain because the run record of P3-04 holds it.
/// </summary>
/// <param name="Candidates">Walks.</param>
/// <param name="Trades">Walks that gave a trade.</param>
/// <param name="NoFillBar">No-trade: see <see cref="NoTradeReason.NoFillBar"/>.</param>
/// <param name="AfterSquareOff">No-trade: see <see cref="NoTradeReason.AfterSquareOff"/>.</param>
/// <param name="EntryThroughStop">No-trade: see <see cref="NoTradeReason.EntryThroughStop"/>.</param>
/// <param name="ExitsStop">Trades that ended at the stop.</param>
/// <param name="ExitsTarget">Trades that ended at the target.</param>
/// <param name="ExitsSquareOff">Trades that ended at the square-off.</param>
/// <param name="ExitsEndOfData">Trades that ended because the data did.</param>
/// <param name="StopGapExits">Stop exits whose minute opened strictly beyond the stop.</param>
/// <param name="TargetGapExits">Target exits whose minute opened strictly beyond the target.</param>
/// <param name="EntriesAfterMissingMinutes">Trades filled in a minute that starts later than the decision instant (minutes were missing or empty).</param>
/// <param name="BarsExamined">Minutes that hold a trade, from the fill minute to the exit minute, both included; a no-trade counts the minute it was decided in (1 for <c>LimitNotReached</c> and <c>EntryThroughStop</c>).</param>
/// <param name="LimitNotReached">No-trade: see <see cref="NoTradeReason.LimitNotReached"/>.</param>
/// <param name="EntriesAtOpen">Trades whose limit order filled at the open of the fill minute.</param>
/// <param name="EntriesAtLimit">Trades whose limit order filled at the limit, the minute having opened beyond it.</param>
/// <param name="AmbiguousMinutes">Exit minutes that reached both the stop and the target with the open strictly between them (taken as a stop).</param>
/// <param name="LateSquareOffs">Square-off exits at a minute after the square-off minute (the minutes in between were absent or empty).</param>
/// <param name="ZeroVolumeMinutes">Minutes with volume 0 passed over between the decision instant and the exit (or the deciding minute of a no-trade).</param>
/// <param name="RiskUnderFourTicks">Trades whose risk is under four ticks.</param>
public sealed record WalkCounters(
    long Candidates,
    long Trades,
    long NoFillBar,
    long AfterSquareOff,
    long EntryThroughStop,
    long ExitsStop,
    long ExitsTarget,
    long ExitsSquareOff,
    long ExitsEndOfData,
    long StopGapExits,
    long TargetGapExits,
    long EntriesAfterMissingMinutes,
    long BarsExamined,
    long LimitNotReached,
    long EntriesAtOpen,
    long EntriesAtLimit,
    long AmbiguousMinutes,
    long LateSquareOffs,
    long ZeroVolumeMinutes,
    long RiskUnderFourTicks)
{
    /// <summary>All zeros: the identity of <see cref="Add"/>.</summary>
    public static WalkCounters Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>The field-by-field sum.</summary>
    public WalkCounters Add(WalkCounters other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new WalkCounters(
            Candidates + other.Candidates,
            Trades + other.Trades,
            NoFillBar + other.NoFillBar,
            AfterSquareOff + other.AfterSquareOff,
            EntryThroughStop + other.EntryThroughStop,
            ExitsStop + other.ExitsStop,
            ExitsTarget + other.ExitsTarget,
            ExitsSquareOff + other.ExitsSquareOff,
            ExitsEndOfData + other.ExitsEndOfData,
            StopGapExits + other.StopGapExits,
            TargetGapExits + other.TargetGapExits,
            EntriesAfterMissingMinutes + other.EntriesAfterMissingMinutes,
            BarsExamined + other.BarsExamined,
            LimitNotReached + other.LimitNotReached,
            EntriesAtOpen + other.EntriesAtOpen,
            EntriesAtLimit + other.EntriesAtLimit,
            AmbiguousMinutes + other.AmbiguousMinutes,
            LateSquareOffs + other.LateSquareOffs,
            ZeroVolumeMinutes + other.ZeroVolumeMinutes,
            RiskUnderFourTicks + other.RiskUnderFourTicks);
    }
}
