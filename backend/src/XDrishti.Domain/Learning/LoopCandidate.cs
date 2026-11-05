namespace XDrishti.Domain.Learning;

/// <summary>One shadow candidate, scored by the arms of the loop test before its outcome was known (spec CF-12, D4).</summary>
/// <param name="Key">The trade key.</param>
/// <param name="Day">The trade day (whole days are resampled).</param>
/// <param name="R">The graded R of the candidate on the series' basis.</param>
/// <param name="FrozenScore">The frozen champion's probability (arm A), or null when it could not be scored.</param>
/// <param name="RetrainedScore">The current champion's probability (arm B), or null when it was not stored.</param>
public sealed record LoopCandidate(string Key, DateOnly Day, double R, double? FrozenScore, double? RetrainedScore);
