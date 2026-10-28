using XDrishti.Domain.Research;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// One revision of an order of the owner's day as the broker's order book shows it (spec P11-01). Read only evidence: it says what the owner did and what became of it, never an instruction. The broker's free text (<see cref="RejectText"/>) is
/// length-capped by the mapper and is data, never markup.
/// </summary>
public sealed record LiveOrder(
    Guid AccountId,
    string BrokerOrderId,
    string Symbol,
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
    string? RejectText,
    DateOnly Date);
