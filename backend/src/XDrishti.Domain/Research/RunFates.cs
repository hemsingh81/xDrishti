namespace XDrishti.Domain.Research;

/// <summary>
/// What became of every candidate outcome of a replay (spec P3-04 Scope 8). Each has exactly one fate, decided in this order: dropped by a flagged day (<see cref="EntriesSuppressed"/>); a trade dated before the window
/// (<see cref="TradesBeforeWindow"/>); an <c>EndOfData</c> trade (<see cref="OpenAtEnd"/>); another trade (<see cref="Trades"/>); a walker no-trade, by reason. Identity, on every fixture: <see cref="Total"/> equals the
/// scanner's <c>Candidates</c> summed over both sides and all segments.
/// </summary>
/// <param name="EntriesSuppressed">Outcomes of candidates dated on a flagged day (a trade or a no-trade), dropped and counted.</param>
/// <param name="TradesBeforeWindow">Trades dated before <c>From</c>, dropped and counted.</param>
/// <param name="OpenAtEnd">Trades dated in the window that the walker closed with <c>EndOfData</c>.</param>
/// <param name="Trades">The other trades dated in the window.</param>
/// <param name="NoFillBar">No-trade: see <see cref="NoTradeReason.NoFillBar"/>.</param>
/// <param name="AfterSquareOff">No-trade: see <see cref="NoTradeReason.AfterSquareOff"/>.</param>
/// <param name="EntryThroughStop">No-trade: see <see cref="NoTradeReason.EntryThroughStop"/>.</param>
/// <param name="LimitNotReached">No-trade: see <see cref="NoTradeReason.LimitNotReached"/>.</param>
public sealed record RunFates(long EntriesSuppressed, long TradesBeforeWindow, long OpenAtEnd, long Trades, long NoFillBar, long AfterSquareOff, long EntryThroughStop, long LimitNotReached)
{
    /// <summary>All zeros: the identity of <see cref="Add"/>.</summary>
    public static RunFates Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>The sum of every fate.</summary>
    public long Total => EntriesSuppressed + TradesBeforeWindow + OpenAtEnd + Trades + NoFillBar + AfterSquareOff + EntryThroughStop + LimitNotReached;

    /// <summary>The walker no-trades, all reasons.</summary>
    public long NoTrade => NoFillBar + AfterSquareOff + EntryThroughStop + LimitNotReached;

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
            LimitNotReached + other.LimitNotReached);
    }
}
