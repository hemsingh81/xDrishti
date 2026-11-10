namespace XDrishti.Domain.Reporting;

/// <summary>The drawdown of the basket index (BK-06 m4).</summary>
/// <param name="MaxDrawdown">The worst fall from a peak (fraction, zero or negative).</param>
/// <param name="LongestDays">The longest run of days from a peak to the next new peak, an open run included.</param>
/// <param name="Open">True when the index is below its last peak now.</param>
/// <param name="DaysSincePeak">Days since the last peak; 0 when the last point is a peak.</param>
public sealed record DrawdownResult(Metric MaxDrawdown, Metric LongestDays, bool Open, int DaysSincePeak);
