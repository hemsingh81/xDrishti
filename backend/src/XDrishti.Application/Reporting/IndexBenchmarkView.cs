namespace XDrishti.Application.Reporting;

/// <summary>The price return of the benchmark index over the same window.</summary>
public sealed record IndexBenchmarkView(Guid? InstrumentId, string? Symbol, bool IsDefault, MetricView Return);
