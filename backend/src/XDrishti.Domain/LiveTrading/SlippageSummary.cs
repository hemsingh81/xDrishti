namespace XDrishti.Domain.LiveTrading;

/// <summary>The statistics of one group of slippage observations (spec P11-02): the mean with its 95 % Student-t interval and the median with its order-statistic interval (null below 6 observations, where none exists).</summary>
public sealed record SlippageSummary(int N, double Mean, double StandardDeviation, double MeanLo, double MeanHi, double Median, double? MedianLo, double? MedianHi, double? MedianCoverage);
