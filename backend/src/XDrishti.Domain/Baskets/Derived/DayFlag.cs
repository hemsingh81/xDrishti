namespace XDrishti.Domain.Baskets.Derived;

/// <summary>The point-in-time flags of one date (the flag known on that date, not today's).</summary>
public readonly record struct DayFlag(DateOnly Date, bool AsmGsm, bool InBan, string Series);
