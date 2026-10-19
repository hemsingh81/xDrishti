namespace XDrishti.Domain.Research;

/// <summary>
/// What became of every candidate outcome of a replay (spec P3-04 Scope 8; ADR 0040, D13; ADR 0042, D14). Each has exactly one fate, decided in this order: dropped by a flagged day (<see cref="EntriesSuppressed"/>); a trade dated before the window
/// (<see cref="TradesBeforeWindow"/>); a swing trade excluded across a flagged boundary (<see cref="ExcludedAcrossDiscontinuity"/>); a trade costing skipped for its quantity (<see cref="SkippedQuantity"/>) and then for its cost (<see cref="SkippedCost"/>, ADR 0042); an <c>EndOfData</c> trade (<see cref="OpenAtEnd"/>); another trade (<see cref="Trades"/>); a walker
/// no-trade, by reason. The order is documentation: the fields are all appended at the end with default 0, so that the positions of the existing fields never move. Identity, on every fixture: <see cref="Total"/> equals the scanner's
/// <c>Candidates</c> summed over both sides and all segments.
/// </summary>
/// <param name="EntriesSuppressed">Outcomes of candidates dated on a flagged day (a trade or a no-trade), dropped and counted.</param>
/// <param name="TradesBeforeWindow">Trades dated before <c>From</c>, dropped and counted.</param>
/// <param name="OpenAtEnd">Trades dated in the window that the walker closed with <c>EndOfData</c>.</param>
/// <param name="Trades">The other trades dated in the window.</param>
/// <param name="NoFillBar">No-trade: see <see cref="NoTradeReason.NoFillBar"/>.</param>
/// <param name="AfterSquareOff">No-trade: see <see cref="NoTradeReason.AfterSquareOff"/>.</param>
/// <param name="EntryThroughStop">No-trade: see <see cref="NoTradeReason.EntryThroughStop"/>.</param>
/// <param name="LimitNotReached">No-trade: see <see cref="NoTradeReason.LimitNotReached"/>.</param>
/// <param name="ExcludedAcrossDiscontinuity">Swing trades dated in the window whose walk ended with the data of a segment that a flagged day follows: neither a trade nor open at the end, excluded and counted (D13).</param>
/// <param name="PositionOpen">No-trade: see <see cref="NoTradeReason.PositionOpen"/>.</param>
/// <param name="SkippedQuantity">Trades dated in the window that sizing gave less than one share (ADR 0042, D13); neither a trade nor open at the end. Always 0 in a gross run.</param>
/// <param name="OrderPendingAtEnd">No-trade: see <see cref="NoTradeReason.OrderPendingAtEnd"/> (ADR 0043, D3).</param>
/// <param name="SkippedCost">Trades dated in the window whose charge in R was above <c>max_cost_R</c> (ADR 0042, D13), after the quantity check. Always 0 in a gross run.</param>
public sealed record RunFates(
    long EntriesSuppressed,
    long TradesBeforeWindow,
    long OpenAtEnd,
    long Trades,
    long NoFillBar,
    long AfterSquareOff,
    long EntryThroughStop,
    long LimitNotReached,
    long ExcludedAcrossDiscontinuity = 0,
    long PositionOpen = 0,
    long SkippedQuantity = 0,
    long SkippedCost = 0,
    long OrderPendingAtEnd = 0)
{
    /// <summary>All zeros: the identity of <see cref="Add"/>.</summary>
    public static RunFates Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>The sum of every fate.</summary>
    public long Total => EntriesSuppressed + TradesBeforeWindow + OpenAtEnd + Trades + NoTrade + ExcludedAcrossDiscontinuity + SkippedQuantity + SkippedCost;

    /// <summary>The walker no-trades, all reasons.</summary>
    public long NoTrade => NoFillBar + AfterSquareOff + EntryThroughStop + LimitNotReached + PositionOpen + OrderPendingAtEnd;

    /// <summary>The field-by-field sum.</summary>
    public RunFates Add(RunFates other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new RunFates(
            EntriesSuppressed + other.EntriesSuppressed,
            TradesBeforeWindow + other.TradesBeforeWindow,
            OpenAtEnd + other.OpenAtEnd,
            Trades + other.Trades,
            NoFillBar + other.NoFillBar,
            AfterSquareOff + other.AfterSquareOff,
            EntryThroughStop + other.EntryThroughStop,
            LimitNotReached + other.LimitNotReached,
            ExcludedAcrossDiscontinuity + other.ExcludedAcrossDiscontinuity,
            PositionOpen + other.PositionOpen,
            SkippedQuantity + other.SkippedQuantity,
            SkippedCost + other.SkippedCost,
            OrderPendingAtEnd + other.OrderPendingAtEnd);
    }
}
