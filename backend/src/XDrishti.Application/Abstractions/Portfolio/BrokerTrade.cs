using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// One NSE equity trade of Dhan's trade history (ADR 0025): the exchange's <see cref="TradeId"/> (the trade's identity together with the date), the order it belongs to, the side, the ticker (<see cref="Symbol"/>; null when Dhan sends
/// none - its <c>customSymbol</c> is a company name, never a ticker), the <see cref="Isin"/> and Dhan's security id, the whole quantity and the price, the <see cref="Charges"/> (the sum of Dhan's six charge fields in rupees; informational) and the India date
/// and time of the trade. A row with no ticker is kept when it has an ISIN or a security id: the use case finds the share in the instrument master.
/// </summary>
public sealed record BrokerTrade(
    string TradeId, string? OrderId, TradeSide Side, string? Symbol, string? Isin, string? SecurityId, long Qty, decimal Price, decimal Charges, DateOnly Date, TimeOnly? Time);
