namespace XDrishti.Domain.Accounts;

/// <summary>The portfolio configuration of an account as the owner entered it (everything except whether the role is on).</summary>
public sealed record PortfolioSpec(string? DefaultBucket, DateOnly? HistoryFrom, bool IncludeInTotals);
