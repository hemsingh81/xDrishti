namespace XDrishti.Domain.Pairs;

/// <summary>The MacKinnon (2010) critical values of the Engle-Granger statistic at a sample size (N = 2 series, constant): reject the unit root when tau is below the value.</summary>
public sealed record CriticalValues(int SampleSize, double Percent1, double Percent5, double Percent10);
