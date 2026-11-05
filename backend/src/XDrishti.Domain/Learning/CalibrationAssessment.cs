using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>
/// How well predicted probabilities match what happened (CF-10 part 10a, ADR 0214): the expected calibration error with a day-cluster bootstrap interval, the mean gap between predicted and observed with its standard error, and the log loss against the constant base rate.
/// </summary>
/// <param name="N">Outcomes.</param>
/// <param name="Days">Distinct trade days (the bootstrap resamples whole days).</param>
/// <param name="MeanPredicted">Mean predicted probability.</param>
/// <param name="Observed">Observed win rate.</param>
/// <param name="GapPp">Mean predicted minus observed, in percentage points (positive: over-confident).</param>
/// <param name="StandardErrorPp">The standard error of the observed rate, <c>sqrt(q (1 - q) / n)</c> with <c>q</c> the observed rate, in percentage points (0 when <c>q</c> is 0 or 1).</param>
/// <param name="EcePp">The point estimate of the expected calibration error over equal-mass bins, in percentage points.</param>
/// <param name="EceLowPp">The lower end of the percentile interval of the bootstrapped error.</param>
/// <param name="EceHighPp">The upper end.</param>
/// <param name="Level">The interval's coverage (0.90 is a 90 % interval).</param>
/// <param name="Draws">Bootstrap resamples.</param>
/// <param name="LogLoss">The mean log loss of the predictions.</param>
/// <param name="BaseRateLogLoss">The mean log loss of the constant that is the observed rate of these very outcomes: the best any constant can do here, so beating it is not flattered.</param>
public sealed record CalibrationAssessment(
    int N,
    int Days,
    double MeanPredicted,
    double Observed,
    double GapPp,
    double StandardErrorPp,
    double EcePp,
    double EceLowPp,
    double EceHighPp,
    double Level,
    int Draws,
    double LogLoss,
    double BaseRateLogLoss)
{
    /// <summary>The log loss minus the base-rate log loss: negative is better than the constant.</summary>
    public double LogLossMinusBaseRate => LogLoss - BaseRateLogLoss;

    /// <summary>One plain line for a report.</summary>
    public string ToText() => string.Create(CultureInfo.InvariantCulture,
        $"ece {EcePp:F2} pp ({Level * 100:F0} % day-cluster bootstrap interval {EceLowPp:F2} to {EceHighPp:F2} pp over {Days} days, {Draws} draws), gap {GapPp:+0.00;-0.00} pp with standard error {StandardErrorPp:F2} pp, logloss minus base rate {LogLossMinusBaseRate:+0.0000;-0.0000}");
}
