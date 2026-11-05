namespace XDrishti.Domain.Learning;

/// <summary>The out-of-sample metrics of one scorer in one fold.</summary>
/// <param name="Name">The scorer.</param>
/// <param name="Metrics">Metrics over the test rows of the fold.</param>
/// <param name="Calibration">The pooled calibration error with its day-cluster bootstrap interval and the log loss against the base rate (CF-10 part 10a); only on the pooled result of a candidate, and null when <c>ece_draws</c> is 0 or the interval could not be computed.</param>
/// <param name="CalibrationUnavailable">The failure code when the interval was asked for and could not be computed; the report prints it, so a missing interval is never silent.</param>
public sealed record ScorerFoldResult(string Name, ProbabilityMetrics Metrics, CalibrationAssessment? Calibration = null, string? CalibrationUnavailable = null);
