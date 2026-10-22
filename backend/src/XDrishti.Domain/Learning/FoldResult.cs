namespace XDrishti.Domain.Learning;

/// <summary>One used fold with the always-take summary and every scorer's metrics (baselines first, then the candidates in the order given).</summary>
/// <param name="Fold">The fold.</param>
/// <param name="AlwaysTake">Take every trade of the test window.</param>
/// <param name="Scorers">Metrics by scorer.</param>
public sealed record FoldResult(ValidationFold Fold, AlwaysTakeSummary AlwaysTake, IReadOnlyList<ScorerFoldResult> Scorers);
