using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>One row of the day's order book after mapping: what the owner placed and what became of it. <see cref="RejectText"/> is the broker's own text, length-capped, data only.</summary>
public sealed record LiveBrokerOrder(
    string OrderId,
    string? Symbol,
    string? Isin,
    string? SecurityId,
    Side Side,
    string Product,
    string OrderType,
    string Validity,
    long Qty,
    decimal? Price,
    decimal? TriggerPrice,
    string Status,
    long FilledQty,
    decimal? AvgTradedPrice,
    DateTimeOffset CreateTime,
    DateTimeOffset? ExchangeTime,
    DateTimeOffset? UpdateTime,
    string? LegName,
    string? AlgoId,
    string? RejectText);
