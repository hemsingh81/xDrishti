namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>A run of whole India dates, <see cref="From"/> to <see cref="To"/> inclusive.</summary>
public readonly record struct BrokerDateRange(DateOnly From, DateOnly To);
