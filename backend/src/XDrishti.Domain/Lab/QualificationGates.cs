namespace XDrishti.Domain.Lab;

/// <summary>The thresholds a strategy variant must meet out of sample (doc 16 <c>qualification_gates</c>, spec P5-03).</summary>
public sealed record QualificationGates(int MinOosTrades, double MinExpectancyR, double MinProfitFactor, double MinPositiveFoldsPct, double MaxDrawdownR, double MaxCalibrationErrorPp)
{
    /// <summary>Doc 16: 30 trades, 0.15 R, profit factor 1.2, 60 % of folds positive, drawdown 10 R, calibration error 5 pp.</summary>
    public static QualificationGates Default { get; } = new(30, 0.15, 1.2, 60, 10, 5);
}
