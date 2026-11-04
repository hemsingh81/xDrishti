namespace XDrishti.Domain.Lab;

/// <summary>
/// The sizing of the locked hold-out (spec CF-06, D4). <paramref name="Status"/> is <c>ok</c>, <c>no_folds</c> (development is shorter than the first training window, so no fold can be formed) or <c>no_history</c> (the hold-out would cover all the data).
/// </summary>
/// <param name="HistoryMonths">Months of stored history (can be fractional).</param>
/// <param name="LengthMonths">Whole months kept: at least the minimum, at least the share of the history, rounded up.</param>
/// <param name="DevelopmentMonths">History left for development.</param>
/// <param name="Folds">Quarterly test folds that fit in development after the first training window.</param>
/// <param name="Start">First day of the hold-out; null when the history has no dates (months-only sizing).</param>
/// <param name="Status">ok, no_folds or no_history.</param>
public sealed record HoldOutPlan(double HistoryMonths, int LengthMonths, double DevelopmentMonths, int Folds, DateOnly? Start, string Status);
