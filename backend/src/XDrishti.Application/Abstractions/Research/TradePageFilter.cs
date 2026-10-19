using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>The closed filter of a page of a run's trades (spec P3-10, D7): equality on the side, the symbol, the exit reason and the list, a range on the trade date. No free text; the symbol is never a pattern.</summary>
public sealed record TradePageFilter(Side? Side = null, string? Symbol = null, ExitReason? ExitReason = null, DateOnly? TradeDateFrom = null, DateOnly? TradeDateTo = null, bool? OpenAtEnd = null);
