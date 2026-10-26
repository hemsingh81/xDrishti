namespace XDrishti.Domain.Paper;

/// <summary>One stored paper entry of one account with its exit (spec P10-03). Plain data from the insert-only paper record.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="ItemId">The ticket.</param>
/// <param name="AccountId">The paper account.</param>
/// <param name="Symbol">The symbol.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="TicketGroup">primary or reserve.</param>
/// <param name="TradeKey">The count-once key.</param>
/// <param name="Measured">Whether the entry counts (measure status measured, not straddle).</param>
/// <param name="QtyAllocated">The proposed quantity.</param>
/// <param name="Qty">The owner's quantity.</param>
/// <param name="EntryAt">The start of the fill minute.</param>
/// <param name="Exit">The exit, or null while open.</param>
public sealed record ReviewTrade(long PlanId, long ItemId, Guid AccountId, string Symbol, string Side, string TicketGroup, string TradeKey, bool Measured, int QtyAllocated, int Qty, DateTimeOffset EntryAt, ReviewExit? Exit)
{
    /// <summary>Whether the replay closed it.</summary>
    public bool Closed => Exit is not null;
}
