namespace XDrishti.Domain.Portfolio;

/// <summary>A stretch of whole India dates, <see cref="From"/> to <see cref="To"/> inclusive, that a run read again (<see cref="BrokerSyncCounts.RereadDone"/>). Dates only: no content of any row.</summary>
public readonly record struct BrokerWindowSpan(DateOnly From, DateOnly To);
