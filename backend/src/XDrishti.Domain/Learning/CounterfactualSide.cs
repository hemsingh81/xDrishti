namespace XDrishti.Domain.Learning;

/// <summary>One side of the counterfactual on a set of rows (P6-12): the P6-05 metrics of its probabilities and the realised R of taking the trades it scores at or above the base rate of its own train set (no searched threshold).</summary>
/// <param name="Metrics">Out-of-sample probability metrics.</param>
/// <param name="Policy">Kept and skipped R; the skipped trades' R is beside the kept ones.</param>
public sealed record CounterfactualSide(ProbabilityMetrics Metrics, MetaPolicyTotals Policy);
