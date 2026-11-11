namespace XDrishti.Domain.Reporting;

/// <summary>The share of members above their 20-day average on one session (BK-07 breadth series); insufficient when too few voted.</summary>
/// <param name="Date">The session.</param>
/// <param name="Share">The share (0 to 1), or why there is none.</param>
public sealed record BreadthPoint(DateOnly Date, Metric Share);
