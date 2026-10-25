namespace XDrishti.Domain.Reporting;

/// <summary>The rules of the buy-and-hold figure (P8-07), set by the caller from settings.</summary>
/// <param name="MinMembers">Fewest included instruments for a number.</param>
/// <param name="MaxStartGapDays">Most days the first bar may follow the window start, or the last bar precede the window end.</param>
/// <param name="DiscontinuityThreshold">Largest one-session close-to-close move (0.25 = 25 %) kept; a larger one is taken for an unadjusted split or bonus.</param>
public sealed record BuyAndHoldOptions(int MinMembers, int MaxStartGapDays, double DiscontinuityThreshold);
