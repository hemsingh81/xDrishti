namespace XDrishti.Domain.Confidence;

/// <summary>One trade of the series the sequential test reads, in order.</summary>
/// <param name="R">The trade's R (net of charges only where the series is labelled so).</param>
/// <param name="DayCandidates">The candidates of the trade's day (<c>m</c> in the design effect); at least 1.</param>
public sealed record SequentialTrade(double R, int DayCandidates);
