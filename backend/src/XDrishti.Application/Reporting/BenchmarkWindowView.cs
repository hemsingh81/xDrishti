namespace XDrishti.Application.Reporting;

/// <summary>The window; <c>Source</c> is <c>query</c> when both ends were given, else <c>runs</c> (taken from the default run set).</summary>
public sealed record BenchmarkWindowView(DateOnly From, DateOnly To, string Source);
