namespace XDrishti.Domain.Learning;

/// <summary>One shadow candidate for the decision-quality lift (spec CF-12, D7).</summary>
/// <param name="Key">The trade key.</param>
/// <param name="Day">The night's trade day (candidates are ranked within their own day).</param>
/// <param name="Score">The model's score stored before the open, or null when none was stored.</param>
/// <param name="R">The graded R, or null while ungraded.</param>
/// <param name="Approved">True when the owner approved a ticket for this candidate.</param>
public sealed record LiftCandidate(string Key, DateOnly Day, double? Score, double? R, bool Approved);
