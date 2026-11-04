namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One family in the P14 report. <see cref="Status"/> is KEEP, DROP, INCONCLUSIVE, NOT_EVALUABLE, stale, none, unreadable or cut; only a DROP with sound evidence, or a KEEP confirmed on the hold-out, is <see cref="Resolved"/>. Shares are null when unavailable.</summary>
public sealed record OptionsFamilyLine(string Family, string Status, bool Resolved, bool Cut, string? RecordId, int? Trials, string Basis, string HoldOut, double? DerivedExpiryShare, double? FallbackShare, string Detail);

/// <summary>A non-cash expression in use: a stored plan item or paper entry with a non-cash cell key. Reported, never blocked.</summary>
public sealed record OptionsViolation(string Source, long Id, string Text);
