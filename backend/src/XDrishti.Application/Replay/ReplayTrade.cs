using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Replay;

/// <summary>
/// One trade of the replay book: the taken item, its filled entry, and its exit once the replay has closed it. The exit is stored when it is found, but <see cref="LedgerAsOf"/> shows it only at and after its own session, so the book at S never knows a later exit.
/// </summary>
internal sealed class ReplayTrade(long id, NewPlanItem item, DateOnly forDate, int qty, Guid? instrumentId, DateTimeOffset entryAt, decimal entryPrice, decimal stopPrice, bool swing, string tradeKey)
{
    /// <summary>A stable id in the order the trades were booked.</summary>
    public long Id { get; } = id;

    /// <summary>The plan item that was taken.</summary>
    public NewPlanItem Item { get; } = item;

    /// <summary>The session the plan was for (the entry session).</summary>
    public DateOnly ForDate { get; } = forDate;

    /// <summary>The quantity of the replay account.</summary>
    public int Qty { get; } = qty;

    /// <summary>The instrument id, when known.</summary>
    public Guid? InstrumentId { get; } = instrumentId;

    /// <summary>The start of the fill minute.</summary>
    public DateTimeOffset EntryAt { get; } = entryAt;

    /// <summary>The filled entry.</summary>
    public decimal EntryPrice { get; } = entryPrice;

    /// <summary>The initial stop.</summary>
    public decimal StopPrice { get; } = stopPrice;

    /// <summary>Whether the cell's style is swing.</summary>
    public bool Swing { get; } = swing;

    /// <summary>The count-once key.</summary>
    public string TradeKey { get; } = tradeKey;

    /// <summary>The exit once found; null while the trade is open.</summary>
    public PaperExit? Exit { get; set; }

    /// <summary>The India date of the entry.</summary>
    public DateOnly EntrySession => IndiaTime.Today(EntryAt);

    /// <summary>The India date of the exit; null while open.</summary>
    public DateOnly? ExitSession => Exit is null ? null : IndiaTime.Today(Exit.ExitAt);

    /// <summary>The trade as the paper ledger reads it at the close of <paramref name="session"/>: its exit only when the exit session is not after it.</summary>
    public LedgerTrade LedgerAsOf(DateOnly session, Guid accountId) => new(
        Id, accountId, Item.Symbol, Item.Side, Qty, EntryAt, EntryPrice, StopPrice, Swing, "primary", TradeKey,
        Exit is { } e && ExitSession <= session ? new LedgerExit(e.ExitAt, e.GrossPnl, e.Charges, e.NetPnl, e.ChargeSheetId, e.RSim) : null);
}
