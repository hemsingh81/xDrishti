using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>One row of the day's trade book after mapping (spec P11-01): any product of the NSE cash segment. The ticker is null when Dhan sent none (the use case finds the share by ISIN or security id).</summary>
public sealed record LiveBrokerFill(string TradeId, string? OrderId, Side Side, string? Symbol, string? Isin, string? SecurityId, string Product, long Qty, decimal Price, DateOnly Date, TimeOnly? Time);
