namespace XDrishti.Domain.Baskets.Derived;

/// <summary>The point-in-time flags of one date (the flag known on that date, not today's). <c>Unknown</c> marks the end of what a list can vouch for (its last good check): from that date on nothing is known, and an unknown flag is never a pass.</summary>
public readonly record struct DayFlag(DateOnly Date, bool AsmGsm, bool InBan, string Series, bool Unknown = false);
