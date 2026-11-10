namespace XDrishti.Domain.Reporting;

/// <summary>The annualised volatility of the basket index over one window (BK-06 m3).</summary>
/// <param name="Window">Index returns in the window.</param>
/// <param name="Volatility">The figure (fraction), or why there is none.</param>
public sealed record VolatilityFigure(int Window, Metric Volatility);
