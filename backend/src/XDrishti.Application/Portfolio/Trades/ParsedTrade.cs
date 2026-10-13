using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// One valid equity trade row of a tradebook file (ADR 0023). <see cref="Line"/> is the line of the file the row starts on. <see cref="TradeId"/> is the exchange's trade id when the file has such a column (it is the trade's identity);
/// <see cref="OrderId"/> is only a part of the identity hash of a row that has no trade id (an order may be filled in several trades).
/// </summary>
internal sealed record ParsedTrade(
    int Line,
    string Symbol,
    string? Isin,
    string Exchange,
    TradeSide Side,
    DateOnly Date,
    TimeOnly? Time,
    long Qty,
    decimal Price,
    decimal Charges,
    string? TradeId,
    string? OrderId)
{
    /// <summary>What makes two rows the same trade when they have no trade id: everything but the line.</summary>
    public string ContentKey => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{Date:yyyy-MM-dd}|{Time:HH:mm:ss.fffffff}|{(Symbol.Length > 0 ? Symbol : Isin)}|{Side}|{Qty}|{Price:0.0000}|{OrderId}");
}
