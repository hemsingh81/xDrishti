namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One family in the report (P15-12). <see cref="Status"/> is KEEP, DROP, INCONCLUSIVE, NOT_EVALUABLE, stale, none, unreadable or cut; only KEEP and DROP with sound evidence are <see cref="Resolved"/>.</summary>
/// <param name="Family">The family.</param>
/// <param name="Status">The status word.</param>
/// <param name="Resolved">KEEP or DROP with its evidence block, for the current versions.</param>
/// <param name="Cut">Left out of the overall by the owner's list.</param>
/// <param name="RecordId">The record the status comes from, or null.</param>
/// <param name="Trials">N of that record, or null.</param>
/// <param name="HoldOut">read, not read or unavailable.</param>
/// <param name="Detail">Why the family is open, or what it rests on.</param>
public sealed record EventsFamilyLine(string Family, string Status, bool Resolved, bool Cut, string? RecordId, int? Trials, string HoldOut, string Detail);
