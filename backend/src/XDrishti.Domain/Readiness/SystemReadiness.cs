namespace XDrishti.Domain.Readiness;

/// <summary>The readiness of the whole system (spec CF-11 D5): one number, the weakest strategy and the weakest sub-score named.</summary>
/// <param name="Score">0 to 100: <c>min(integrity, weighted mean of the strategy scores)</c>.</param>
/// <param name="Label">The label of the score.</param>
/// <param name="Integrity">100 when the integrity checks are clear, else 0 (an unavailable reading is 0, never a pass).</param>
/// <param name="WeightedMean">The weighted mean of the strategy scores; 0 when there is no strategy.</param>
/// <param name="EqualWeights">True when the weights are equal because the plan exposes no risk share for every strategy (labelled).</param>
/// <param name="WeakestStrategy">The strategy with the lowest score, or null when there is none.</param>
/// <param name="WeakestSubScore">That strategy's weakest sub-score, or null.</param>
/// <param name="Strategies">How many strategies were combined.</param>
/// <param name="Reason">Why the score is what it is when it is 0 for a reason other than the strategies (integrity, no strategy).</param>
public sealed record SystemReadiness(double Score, ReadinessLabel Label, double Integrity, double WeightedMean, bool EqualWeights, string? WeakestStrategy, string? WeakestSubScore, int Strategies, string? Reason);
