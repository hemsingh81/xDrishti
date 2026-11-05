using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Confidence;

/// <summary>The candidates the arms of the loop test scored, and what the take rule needs (spec CF-12 D4).</summary>
/// <param name="Candidates">Shadow candidates with the graded R and the arms' stored scores.</param>
/// <param name="FrozenBaseRate">The frozen champion's own train base rate, or null.</param>
/// <param name="RetrainedBaseRate">The current champion's own train base rate, or null.</param>
/// <param name="SeedKey">What the bootstrap seed is made of (the strategy or scope, the champions' hashes and the span).</param>
public sealed record LoopArmsInput(IReadOnlyList<LoopCandidate> Candidates, double? FrozenBaseRate, double? RetrainedBaseRate, string SeedKey);

/// <summary>
/// What the loop report can be computed from. A section that is null is missing, and its <c>Reason</c> says why.
/// </summary>
/// <param name="Arms">Input of the three-arm test.</param>
/// <param name="ArmsReason">Why there is none.</param>
/// <param name="Curve">The points of the learning curve (gains of models refit on growing rows).</param>
/// <param name="CurveReason">Why there are none.</param>
/// <param name="Outcomes">The model's outcomes with the probabilities stored before them.</param>
/// <param name="OutcomesReason">Why there are none.</param>
/// <param name="Lift">The shadow candidates with their stored scores and approvals.</param>
/// <param name="LiftReason">Why there are none.</param>
public sealed record LoopEvidence(
    LoopArmsInput? Arms,
    string? ArmsReason,
    IReadOnlyList<LearningCurvePoint>? Curve,
    string? CurveReason,
    IReadOnlyList<CalibrationObservation>? Outcomes,
    string? OutcomesReason,
    IReadOnlyList<LiftCandidate>? Lift,
    string? LiftReason);
