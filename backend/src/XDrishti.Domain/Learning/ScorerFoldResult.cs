namespace XDrishti.Domain.Learning;

/// <summary>The out-of-sample metrics of one scorer in one fold.</summary>
/// <param name="Name">The scorer.</param>
/// <param name="Metrics">Metrics over the test rows of the fold.</param>
public sealed record ScorerFoldResult(string Name, ProbabilityMetrics Metrics);
