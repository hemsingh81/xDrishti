using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Readiness;

/// <summary>
/// The anchors of the six sub-scores (spec CF-11 D2): piecewise linear between the points the spec names, clamped to 0 and 100 outside them. Pure. The anchors are not settings (they interact; see <see cref="ReadinessOptions"/>); the rules version in the settings text
/// changes when they do.
/// </summary>
public static class ScoreAnchors
{
    /// <summary>The effect the power sub-score is asked about, in R.</summary>
    public const double TargetEffectR = 0.3;

    /// <summary>The standard deviation of a trade's R the power sub-score assumes.</summary>
    public const double AssumedSigma = 1.5;

    /// <summary>The one-sided critical value of the power formula (alpha 5 %), as the spec writes it.</summary>
    public const double CriticalZ = 1.645;

    /// <summary>The power the power sub-score gives 100 for.</summary>
    public const double FullPower = 0.80;

    /// <summary>The effective n the evidence sub-score gives 100 for.</summary>
    public const double FullNEff = 100;

    /// <summary>The effect lower bound the effect sub-score gives 100 for, in R.</summary>
    public const double FullEffectR = 0.15;

    /// <summary>The calibration error the calibration sub-score gives 100 for, in percentage points.</summary>
    public const double FullCalibrationPp = 3;

    /// <summary>The share of qualifying months that must be positive for the backtest-agreement sub-score to reach 100.</summary>
    public const double PositiveMonthShare = 0.70;

    /// <summary>The fewest trades a month needs to count in that share.</summary>
    public const int MonthMinTrades = 5;

    /// <summary>Folds at which the stability sub-score is no longer capped at 50.</summary>
    public const int FullFolds = 6;

    /// <summary>The z of the lower bound of the effect: the 5th percentile of the mean (the project's "90 % interval").</summary>
    public const double LowerBoundZ = 1.645;

    /// <summary>Evidence: effective n 15 gives 0, 30 gives 50, 100 gives 100.</summary>
    public static double Evidence(double nEff) => Interpolate(nEff, (15, 0), (30, 50), (FullNEff, 100));

    /// <summary>Effect: the lower bound of the mean net R, 0 gives 0, 0.10 gives 50, 0.15 gives 100.</summary>
    public static double Effect(double lowerBoundR) => Interpolate(lowerBoundR, (0, 0), (0.10, 50), (FullEffectR, 100));

    /// <summary>Power at 0.3 R: 30 % gives 0, 50 % gives 50, 80 % gives 100 (the argument is a probability).</summary>
    public static double PowerScore(double power) => Interpolate(power, (0.30, 0), (0.50, 50), (FullPower, 100));

    /// <summary>Calibration: an expected calibration error of 10 pp gives 0, 5 pp gives 50, 3 pp gives 100.</summary>
    public static double Calibration(double ecePp) => Interpolate(ecePp, (FullCalibrationPp, 100), (5, 50), (10, 0));

    /// <summary>The power of the test of a true effect of <see cref="TargetEffectR"/> with <paramref name="nEff"/> effective trades: <c>P(Z &gt; 1.645 - 0.3 sqrt(n_eff) / 1.5)</c>.</summary>
    public static double Power(double nEff) => Power(nEff, TargetEffectR);

    /// <summary>The same for a true effect of <paramref name="delta"/> R (the P10-00 D7 check uses 0.2 and 0.5 as well).</summary>
    public static double Power(double nEff, double delta) => nEff <= 0 ? 0 : 1 - NormalDistribution.Cdf(CriticalZ - (delta * Math.Sqrt(nEff) / AssumedSigma));

    /// <summary>The effective n at which the power reaches <see cref="FullPower"/> (the inverse of <see cref="Power(double)"/>).</summary>
    public static double NEffForFullPower() => Math.Pow((CriticalZ + NormalDistribution.Quantile(FullPower)) * AssumedSigma / TargetEffectR, 2);

    /// <summary>The lower bound of the mean: <c>mean - 1.645 x sd / sqrt(n_eff)</c>.</summary>
    public static double LowerBound(double mean, double sd, double nEff) => mean - (LowerBoundZ * sd / Math.Sqrt(nEff));

    /// <summary>Piecewise linear interpolation between points sorted by ascending X, clamped to the end values outside the first and last point.</summary>
    public static double Interpolate(double x, params (double X, double Y)[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length == 0)
        {
            throw new ArgumentException("At least one anchor is needed.", nameof(points));
        }

        if (double.IsNaN(x))
        {
            return 0;
        }

        if (x <= points[0].X)
        {
            return points[0].Y;
        }

        for (var i = 1; i < points.Length; i++)
        {
            if (x <= points[i].X)
            {
                var (x0, y0) = points[i - 1];
                var (x1, y1) = points[i];
                return y0 + ((x - x0) / (x1 - x0) * (y1 - y0));
            }
        }

        return points[^1].Y;
    }
}
