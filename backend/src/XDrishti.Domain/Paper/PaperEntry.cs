namespace XDrishti.Domain.Paper;

/// <summary>
/// The entry of one paper trade for one account (spec P10-01, ADR 0116): what the replay filled and the owner's quantity. A value, not an order: the record says so in <see cref="PaperWording.Basis"/>. Immutable once stored.
/// </summary>
/// <param name="PlanId">The plan.</param>
/// <param name="PlanItemId">The ticket.</param>
/// <param name="AccountId">The paper account.</param>
/// <param name="Symbol">The symbol as of the plan.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="StrategyHash">The strategy version.</param>
/// <param name="CellKey">The cell of the ticket.</param>
/// <param name="TicketGroup">primary or reserve.</param>
/// <param name="ApprovalId">The approval row of the ticket.</param>
/// <param name="ApprovalKind">owner or auto.</param>
/// <param name="QtyAllocated">The allocated quantity.</param>
/// <param name="Qty">The quantity after the owner's size-down, at least 1 and at most the allocated one.</param>
/// <param name="ArmedAt">When the ticket was armed.</param>
/// <param name="EntryAt">The start of the fill minute.</param>
/// <param name="EntryPrice">The filled entry (after modelled slippage and the tick grid).</param>
/// <param name="EntryExact">The exact entry before slippage.</param>
/// <param name="StopPrice">The initial stop.</param>
/// <param name="TargetPrice">The target, or null.</param>
/// <param name="Swing">Whether the cell's style is swing.</param>
/// <param name="TradeKey">The count-once key <c>StrategyId|Symbol|Side|DecisionDate</c>.</param>
/// <param name="RunKey">The key of the replay run.</param>
/// <param name="EngineVersion">The engine that ran.</param>
public sealed record PaperEntry(
    long PlanId,
    long PlanItemId,
    Guid AccountId,
    string Symbol,
    string Side,
    string StrategyId,
    string StrategyHash,
    string CellKey,
    string TicketGroup,
    long ApprovalId,
    string ApprovalKind,
    int QtyAllocated,
    int Qty,
    DateTimeOffset ArmedAt,
    DateTimeOffset EntryAt,
    decimal EntryPrice,
    decimal EntryExact,
    decimal StopPrice,
    decimal? TargetPrice,
    bool Swing,
    string TradeKey,
    string RunKey,
    string EngineVersion)
{
    /// <summary>Whether an entry at <paramref name="entryAt"/> is allowed for a ticket armed at <paramref name="armedAt"/>: never before arming (the database repeats it).</summary>
    public static bool EntersAfterArming(DateTimeOffset armedAt, DateTimeOffset entryAt) => entryAt >= armedAt;

    /// <summary>Whether a later replay shows the same entry as this stored one: the same minute at the same filled price.</summary>
    public bool SameEntryAs(DateTimeOffset entryAt, decimal entryPrice) => EntryAt == entryAt && Math.Round(EntryPrice, 6) == Math.Round(entryPrice, 6);
}
