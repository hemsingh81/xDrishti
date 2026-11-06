using XDrishti.Domain.Readiness;

namespace XDrishti.Application.Confidence;

/// <summary>What the forward evidence of one strategy gives the scorecard: the score inputs, the forward figures for row 15, and the hard-fail terms it can read.</summary>
/// <param name="Score">The sub-score inputs that are read (evidence, effect, agreement, calibration, drift); the folds come from the Lab.</param>
/// <param name="ForwardMeanR">The forward mean net R, or null.</param>
/// <param name="ForwardStandardError">Its standard error, or null.</param>
/// <param name="PhaseCheckMet">Whether the P10 phase check reads MET; null when it cannot be read or is not evaluable.</param>
/// <param name="FailureBoundary">The CF-09 hard-fail term.</param>
/// <param name="KillDemotion">The CF-12 hard-fail term.</param>
/// <param name="Notes">The honesty sentences about what was read.</param>
internal sealed record ForwardEvidence(ScoreInputs Score, double? ForwardMeanR, double? ForwardStandardError, bool? PhaseCheckMet, FaultState FailureBoundary, FaultState KillDemotion, IReadOnlyList<string> Notes);
