namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The tolerances of the flags (spec P11-01 decision 5, <c>Live:Match</c>): they flag, they never reject a match. A chased entry is worse than the plan entry by more than <see cref="ChasedEntryR"/> of the planned risk (or by more than
/// <see cref="ChasedEntryPct"/> percent of the entry when the plan has no stop); an entry below <see cref="SizeBelowPct"/> percent of the allocation, with no order that explains it, is flagged.
/// </summary>
public sealed record MatchTolerances(decimal ChasedEntryR, decimal ChasedEntryPct, decimal SizeBelowPct)
{
    public static MatchTolerances Default { get; } = new(0.25m, 1.0m, 90m);
}
