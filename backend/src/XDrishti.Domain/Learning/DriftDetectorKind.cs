namespace XDrishti.Domain.Learning;

/// <summary>Which monitor an event comes from (P6-09).</summary>
public enum DriftDetectorKind
{
    /// <summary>Rolling expectancy below the credible band of the reference period.</summary>
    RollingBand,

    /// <summary>A one-sided CUSUM of R against the reference mean.</summary>
    Cusum,

    /// <summary>Mean predicted against observed win rate of a registered model on a rolling window.</summary>
    Calibration,
}
