namespace XDrishti.Domain.Learning;

/// <summary>A candidate's metrics beside the two baselines of P6-00 D4 on the same rows (one fold, or the pooled rows).</summary>
/// <param name="Candidate">The candidate.</param>
/// <param name="CellRate">The cell-statistics baseline.</param>
/// <param name="Logistic">The logistic baseline.</param>
public sealed record BaselineComparison(ProbabilityMetrics Candidate, ProbabilityMetrics CellRate, ProbabilityMetrics Logistic);
