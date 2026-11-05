namespace XDrishti.Domain.Learning;

/// <summary>One closed trade of a forward series as the decay monitor reads it (spec CF-12, D1).</summary>
/// <param name="Key">A stable id (the trade key); breaks ties in time.</param>
/// <param name="KnownAtEpochSeconds">The instant the result became known (the exit, or the session's close for a graded shadow trade).</param>
/// <param name="Day">The trade day, for the by-quarter table.</param>
/// <param name="R">Net R after the stated basis.</param>
/// <param name="DayCandidates">How many candidates the trade's day had (the design effect of CF-01 is built from it).</param>
public sealed record DecayTrade(string Key, long KnownAtEpochSeconds, DateOnly Day, double R, int DayCandidates);
