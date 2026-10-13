using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// A stored trade as the reconstruction reads it. The order of trades is: date, buys before sells on the same day, time (a trade without time counts as midnight), then the order they were stored in
/// (<see cref="CreatedAt"/>, the line of the file <see cref="Seq"/>, the id) — so the same trades always give the same lots.
/// </summary>
internal sealed record ReconTrade(DateOnly Date, TimeOnly? Time, TradeSide Side, long Qty, decimal Price, DateTimeOffset CreatedAt, int Seq, Guid Id)
{
    public static ReconTrade Of(Transaction trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        return new ReconTrade(trade.TradeDate, trade.TradeTime, trade.Side, trade.Qty, trade.Price, trade.CreatedAt, trade.Seq, trade.Id);
    }
}
