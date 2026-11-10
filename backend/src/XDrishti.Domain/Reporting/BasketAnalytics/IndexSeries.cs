namespace XDrishti.Domain.Reporting;

/// <summary>
/// The daily-rebalanced equal-weight index of a basket (BK-06 m1b). <see cref="Points"/> is empty unless <see cref="Status"/> is a value; the first point is the base (level 100, no return), so n index points hold n-1 returns.
/// </summary>
/// <param name="Status">The last level when the index is sound; otherwise why not.</param>
/// <param name="Points">The base point and every day that had enough members.</param>
/// <param name="LeftOut">Instrument-days removed for a discontinuity.</param>
/// <param name="DroppedCloses">Closes dropped as not a positive finite number or as ambiguous.</param>
/// <param name="Days">Sessions after the base.</param>
/// <param name="DaysWithReturn">Sessions that had enough members.</param>
public sealed record IndexSeries(Metric Status, IReadOnlyList<IndexPoint> Points, IReadOnlyList<LeftOutDay> LeftOut, int DroppedCloses, int Days, int DaysWithReturn);
