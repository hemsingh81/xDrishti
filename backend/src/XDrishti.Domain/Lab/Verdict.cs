namespace XDrishti.Domain.Lab;

/// <summary>The result of <see cref="VerdictEvaluator"/>: the outcome, one result per gate, the variant count and factor used, and the honest limits that capped the outcome.</summary>
public sealed record Verdict(VerdictOutcome Outcome, IReadOnlyList<GateResult> Gates, int VariantCount, double AdjustmentFactor, IReadOnlyList<string> Limits);
