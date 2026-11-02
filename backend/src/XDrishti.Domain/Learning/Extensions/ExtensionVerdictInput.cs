namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The measured quantities that the verdict table reads (P13-05). A null is <c>unavailable</c>: it never passes a rule.</summary>
/// <param name="EvaluableFolds">Folds in which both models had a threshold.</param>
/// <param name="PairedCandidates">Candidates in those folds.</param>
/// <param name="Flips">Candidates where A and B decide differently.</param>
/// <param name="PositiveFolds">Evaluable folds with a gain above zero.</param>
/// <param name="Lower">Lower bound of the gain at the stated block.</param>
/// <param name="Upper">Upper bound at the stated block.</param>
/// <param name="LowerSensitivity">Lower bound at the sensitivity block; null when unavailable.</param>
/// <param name="UpperSensitivity">Upper bound at the sensitivity block; null when unavailable.</param>
/// <param name="SensitivityUsed">Whether a sensitivity block is part of the rule.</param>
/// <param name="DeltaLogLoss">Pooled log loss of B minus A.</param>
/// <param name="DeltaBrier">Pooled Brier score of B minus A.</param>
/// <param name="EceBPp">Expected calibration error of B, percentage points.</param>
/// <param name="Factor">The multiple-testing factor.</param>
public sealed record ExtensionVerdictInput(
    int EvaluableFolds,
    int PairedCandidates,
    int Flips,
    int PositiveFolds,
    double? Lower,
    double? Upper,
    double? LowerSensitivity,
    double? UpperSensitivity,
    bool SensitivityUsed,
    double? DeltaLogLoss,
    double? DeltaBrier,
    double? EceBPp,
    double Factor);
