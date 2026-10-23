namespace XDrishti.Domain.Learning;

/// <summary>
/// The knobs of the drift monitors (P6-09, ADR 0074). <c>window_trades</c> and <c>below_band_checks</c> are the defaults of doc 16 <c>drift</c>; <c>max_calibration_error_pp</c> is the gate of doc 16. All are printed with the result.
/// </summary>
/// <param name="WindowTrades">Trades in the rolling window of the band detector (doc 16 <c>drift.window_trades</c>, 20).</param>
/// <param name="BelowBandChecks">Consecutive checks below the band that fire the alarm (doc 16 <c>drift.below_band_checks</c>, 3).</param>
/// <param name="ClearChecks">Consecutive checks back inside the clear level that clear an alarm (hysteresis).</param>
/// <param name="CheckEveryTrades">A check is made after this many new trades (default 20: windows of consecutive checks do not overlap).</param>
/// <param name="MinReferenceTrades">The reference (backtest) period needs this many trades to give a band (at least <see cref="WindowTrades"/>; doc 16 <c>min_trades</c>, 30).</param>
/// <param name="BandZ">The band is the reference mean minus this many standard errors of the difference between a window mean and the estimated reference mean, sd x sqrt(1/window + 1/reference) (1.645 is the 5 % one-sided level of a Gaussian).</param>
/// <param name="CusumAllowance">The CUSUM allowance k in reference standard deviations: shifts smaller than twice this are not chased (0.5).</param>
/// <param name="CusumThreshold">The CUSUM fires when the sum exceeds this many standard deviations h (10, a judgement default chosen for a target, not a measured property: a stationary cell should alarm with probability about 5 % or less over 120 monitored trades and about 10 % or less over 500, with 30 or more reference trades; measured rates are in ADR 0074).</param>
/// <param name="CusumClearFraction">An alarm clears when the sum is at or below this share of h.</param>
/// <param name="CusumCap">The sum is held at this many times h so that a long bad spell can still be left.</param>
/// <param name="CalibrationWindow">Scored outcomes in the calibration window.</param>
/// <param name="CalibrationCheckEvery">A calibration check is made after this many new outcomes.</param>
/// <param name="CalibrationBreachChecks">Consecutive breaches that fire the calibration alarm.</param>
/// <param name="MaxCalibrationGapPp">The gap, in percentage points, between mean predicted and observed win rate that is a breach (doc 16 <c>max_calibration_error_pp</c>, 5).</param>
/// <param name="CalibrationZ">...and the gap must also exceed this many standard errors of the observed rate, because a window of a hundred outcomes cannot see 5 pp.</param>
public sealed record DriftOptions(
    int WindowTrades = 20,
    int BelowBandChecks = 3,
    int ClearChecks = 3,
    int CheckEveryTrades = 20,
    int MinReferenceTrades = 30,
    double BandZ = 1.645,
    double CusumAllowance = 0.5,
    double CusumThreshold = 10,
    double CusumClearFraction = 0.5,
    double CusumCap = 2,
    int CalibrationWindow = 100,
    int CalibrationCheckEvery = 50,
    int CalibrationBreachChecks = 3,
    double MaxCalibrationGapPp = 5,
    double CalibrationZ = 2.5)
{
    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        Whole(problems, "window_trades", WindowTrades, 5, 10000);
        Whole(problems, "below_band_checks", BelowBandChecks, 1, 100);
        Whole(problems, "clear_checks", ClearChecks, 1, 100);
        Whole(problems, "check_every_trades", CheckEveryTrades, 1, 10000);
        Whole(problems, "min_reference_trades", MinReferenceTrades, 5, 100000);
        Whole(problems, "calibration_window", CalibrationWindow, 20, 100000);
        Whole(problems, "calibration_check_every", CalibrationCheckEvery, 1, 100000);
        Whole(problems, "calibration_breach_checks", CalibrationBreachChecks, 1, 100);
        Number(problems, "band_z", BandZ, 0, 10);
        Number(problems, "cusum_allowance", CusumAllowance, 0, 5);
        Number(problems, "cusum_threshold", CusumThreshold, 0.5, 100);
        Number(problems, "cusum_clear_fraction", CusumClearFraction, 0, 1);
        Number(problems, "cusum_cap", CusumCap, 1, 100);
        Number(problems, "max_calibration_gap_pp", MaxCalibrationGapPp, 0, 100);
        Number(problems, "calibration_z", CalibrationZ, 0, 10);
        if (MinReferenceTrades < WindowTrades)
        {
            problems.Add("min_reference_trades must be at least window_trades");
        }

        return problems;
    }

    private static void Whole(List<string> problems, string name, int value, int low, int high)
    {
        if (value < low || value > high)
        {
            problems.Add($"{name} must be {low} to {high}");
        }
    }

    private static void Number(List<string> problems, string name, double value, double low, double high)
    {
        if (!double.IsFinite(value) || value < low || value > high)
        {
            problems.Add($"{name} must be {low} to {high}");
        }
    }
}
