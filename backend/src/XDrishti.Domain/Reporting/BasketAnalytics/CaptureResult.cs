namespace XDrishti.Domain.Reporting;

/// <summary>Up and down capture of the basket index against the benchmark (BK-06 m7).</summary>
/// <param name="Up">Mean basket return over mean index return on days the index rose.</param>
/// <param name="Down">The same on days the index fell.</param>
public sealed record CaptureResult(Metric Up, Metric Down);
