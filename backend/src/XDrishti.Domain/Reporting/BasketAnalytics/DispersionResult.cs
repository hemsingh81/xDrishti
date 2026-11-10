namespace XDrishti.Domain.Reporting;

/// <summary>Cross-sectional dispersion of member returns (BK-06 m10).</summary>
/// <param name="Stdev">Sample stdev of the member returns on the day (fraction).</param>
/// <param name="MeanStdev">The mean of that stdev over the last days.</param>
/// <param name="Iqr">The interquartile range of the member returns on the day.</param>
/// <param name="Voters">Members with a return on the day.</param>
public sealed record DispersionResult(Metric Stdev, Metric MeanStdev, Metric Iqr, int Voters);
