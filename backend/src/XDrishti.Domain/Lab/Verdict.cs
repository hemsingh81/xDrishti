using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The result of <see cref="VerdictEvaluator"/>: the outcome, one result per gate, the variant count and factor used, and the honest limits that capped the outcome. <paramref name="Notes"/> (CF-01) are remarks that never cap or change the outcome,
/// for example how the trades gate would read on effective n. <paramref name="Dependence"/> is the evidence's block with <c>EnoughByNEff</c> judged against the adjusted minimum of gate 1 (the factor is only known here).
/// </summary>
public sealed record Verdict(VerdictOutcome Outcome, IReadOnlyList<GateResult> Gates, int VariantCount, double AdjustmentFactor, IReadOnlyList<string> Limits, IReadOnlyList<string>? Notes = null, DependenceAssessment? Dependence = null);
