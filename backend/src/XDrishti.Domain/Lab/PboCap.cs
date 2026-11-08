namespace XDrishti.Domain.Lab;

/// <summary>The outcome after the PBO gate of CF-03 part B (<see cref="VerdictEvaluator.CapByPbo"/>) and, when it capped, the reason to add to the verdict's limits.</summary>
/// <param name="Outcome">The outcome: never better than the one given.</param>
/// <param name="Reason">Why a PASS was capped; null when nothing changed.</param>
public sealed record PboCap(VerdictOutcome Outcome, string? Reason);
