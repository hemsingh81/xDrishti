namespace XDrishti.Domain.Reporting;

/// <summary>Beta, alpha, tracking error and information ratio of the basket index over one window of common days (BK-06 m5, m6).</summary>
/// <param name="Window">Common days in the window.</param>
/// <param name="Beta">Cov(basket, index) / Var(index).</param>
/// <param name="Alpha">Annualised intercept, rate ignored (fraction).</param>
/// <param name="TrackingError">Annualised stdev of the active return (fraction).</param>
/// <param name="InformationRatio">Annualised mean active return over the tracking error.</param>
public sealed record BetaFigure(int Window, Metric Beta, Metric Alpha, Metric TrackingError, Metric InformationRatio);
