using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// One NSE equity trade of Dhan's trade history (ADR 0025): the exchange's <see cref="TradeId"/> (the trade's identity together with the date), the order it belongs to, the side, the ticker and Dhan's security id, the whole quantity and the price,
/// and the India date and time of the trade. Dhan's trade API carries no ISIN and no charges.
/// </summary>
public sealed record BrokerTrade(string TradeId, string? OrderId, TradeSide Side, string Symbol, string? SecurityId, long Qty, decimal Price, DateOnly Date, TimeOnly? Time);
