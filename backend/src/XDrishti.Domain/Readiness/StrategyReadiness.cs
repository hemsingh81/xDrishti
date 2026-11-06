namespace XDrishti.Domain.Readiness;

/// <summary>The input of the system level for one strategy (spec CF-11 D5).</summary>
/// <param name="Strategy">The strategy id.</param>
/// <param name="Score">Its readiness score (0 to 100).</param>
/// <param name="WeakestSubScore">The name of its weakest sub-score, or null when none could be named.</param>
/// <param name="RiskShare">Its planned risk share (0 to 1), or null when the plan exposes none.</param>
public sealed record StrategyReadiness(string Strategy, double Score, string? WeakestSubScore, double? RiskShare);
