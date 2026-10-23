namespace XDrishti.Domain.Allocation;

/// <summary>The ticket to split. A null number is "not known" and makes the whole allocation unavailable.</summary>
/// <param name="Symbol">The instrument.</param>
/// <param name="Side"><c>BUY</c> or <c>SELL</c>, exactly.</param>
/// <param name="EntryPrice">Planned entry, above 0.</param>
/// <param name="StopPrice">Initial stop: below the entry for a BUY, above it for a SELL.</param>
/// <param name="Quantity">Most shares (or units) to hand out in all, at least one lot.</param>
/// <param name="LotSize">Quantity step, at least 1 (1 for cash equities).</param>
public sealed record AllocationTicket(string Symbol, string Side, double? EntryPrice, double? StopPrice, int? Quantity, int? LotSize);
