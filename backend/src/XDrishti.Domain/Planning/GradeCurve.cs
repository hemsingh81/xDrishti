namespace XDrishti.Domain.Planning;

/// <summary>The top-K curve by rank: its points day by day and its totals.</summary>
/// <param name="Name">top-1, top-3, top-5 or top-all.</param>
/// <param name="Points">One per plan-day, by date then plan.</param>
/// <param name="Totals">The figures over all days.</param>
public sealed record GradeCurve(string Name, IReadOnlyList<CurvePoint> Points, GradeStats Totals);
