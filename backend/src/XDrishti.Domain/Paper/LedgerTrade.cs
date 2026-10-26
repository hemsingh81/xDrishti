namespace XDrishti.Domain.Paper;

/// <summary>
/// One stored paper trade of one account with its exit when the replay closed it (spec P10-02): the only input of <see cref="PaperLedger"/>. Plain data read from the insert-only paper tables; nothing here is an order.
/// </summary>
/// <param name="EntryId">The entry's id (a stable tie-break).</param>
/// <param name="AccountId">The paper account.</param>
/// <param name="Symbol">The symbol.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="Qty">The owner's quantity.</param>
/// <param name="EntryAt">The start of the fill minute.</param>
/// <param name="EntryPrice">The filled entry.</param>
/// <param name="StopPrice">The initial stop.</param>
/// <param name="Swing">Whether the cell's style is swing.</param>
/// <param name="TicketGroup">primary or reserve.</param>
/// <param name="TradeKey">The count-once key <c>StrategyId|Symbol|Side|DecisionDate</c>.</param>
/// <param name="Exit">The exit, or null while the trade is open.</param>
public sealed record LedgerTrade(
    long EntryId,
    Guid AccountId,
    string Symbol,
    string Side,
    int Qty,
    DateTimeOffset EntryAt,
    decimal EntryPrice,
    decimal StopPrice,
    bool Swing,
    string TicketGroup,
    string TradeKey,
    LedgerExit? Exit)
{
    /// <summary>Whether the replay has closed the trade.</summary>
    public bool Closed => Exit is not null;

    /// <summary>Whether it is a buy.</summary>
    public bool IsBuy => string.Equals(Side, "BUY", StringComparison.Ordinal);

    /// <summary>Rupees at the entry price: <c>qty x entry</c>.</summary>
    public decimal Notional => Qty * EntryPrice;

    /// <summary>Rupees at risk to the initial stop: <c>qty x |entry - stop|</c>.</summary>
    public decimal RiskAmount => Qty * Math.Abs(EntryPrice - StopPrice);
}
