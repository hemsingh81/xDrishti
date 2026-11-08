namespace XDrishti.Application.Lab;

/// <summary>
/// The strategy against the buy-and-hold of the same universe over the same days (CF-11 row 13, ADR 0227), measured when a full Lab run ends and carried on its verdict. Report-only: nothing that judges the verdict reads it; only the readiness scorecard's row 13 does. It carries the two daily
/// Sharpe ratios, not a verdict: the row passes only when the strategy's is strictly greater (<c>BenchmarkComparison.Beats</c>). Null on the verdict of a run stored before it.
/// </summary>
/// <param name="Days">The days compared (sessions on which every member has a close, from the first to the last exit of the out-of-sample trades).</param>
/// <param name="Members">The instruments of the run's universe.</param>
/// <param name="StrategySharpe">The daily Sharpe of the out-of-sample net R summed by exit day; null when not measured.</param>
/// <param name="HoldSharpe">The daily Sharpe of the equal-weight hold of the members over the same days; null when not measured.</param>
/// <param name="Reason">Why there is no comparison (too few days, a member left out, no variance, a failed read); empty when there is one.</param>
/// <param name="Basis">What is measured and what is not.</param>
public sealed record BenchmarkComparisonView(int Days, int Members, double? StrategySharpe, double? HoldSharpe, string Reason, string Basis);
