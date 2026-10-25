namespace XDrishti.Application.Reporting;

/// <summary>The window. Each bound says where it came from, <c>query</c> or <c>runs</c> (the default run set); <c>Source</c> is <c>query</c> only when both did.</summary>
public sealed record BenchmarkWindowView(DateOnly From, DateOnly To, string Source, string FromSource, string ToSource);
