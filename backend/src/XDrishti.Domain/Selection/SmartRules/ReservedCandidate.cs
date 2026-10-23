namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>A ranked candidate a smart rule moved to the reserve, with the reason and the numbers.</summary>
/// <param name="Ranked">The candidate with its original rank.</param>
/// <param name="Code">Stable reason code.</param>
/// <param name="Reason">In words, with the numbers.</param>
public sealed record ReservedCandidate(RankedCandidate Ranked, string Code, string Reason);
