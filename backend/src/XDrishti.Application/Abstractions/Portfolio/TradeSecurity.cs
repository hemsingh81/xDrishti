namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>A security as a trade names it: its ISIN (null when the trade has none) and its trading symbol.</summary>
public sealed record TradeSecurity(string? Isin, string TradingSymbol);
