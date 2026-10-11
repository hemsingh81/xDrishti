namespace XDrishti.Application.Portfolio.Returns;

/// <summary>Money out (negative) or in (positive) on a day. Amounts are in rupees.</summary>
public readonly record struct CashFlow(DateOnly Date, decimal Amount);
