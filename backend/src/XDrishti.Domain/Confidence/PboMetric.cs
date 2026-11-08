namespace XDrishti.Domain.Confidence;

/// <summary>The score that picks the in-sample best variant and ranks it out of sample in <see cref="Cscv"/> (CF-03 part B, <c>Confidence:Pbo:Metric</c>).</summary>
public enum PboMetric
{
    /// <summary>The mean of the daily R (the default).</summary>
    Mean = 0,

    /// <summary>The mean over the sample standard deviation of the daily R.</summary>
    Sharpe = 1,
}
