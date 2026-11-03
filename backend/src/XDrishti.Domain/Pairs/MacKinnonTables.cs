using XDrishti.Domain.Common;

namespace XDrishti.Domain.Pairs;

/// <summary>
/// The pinned MacKinnon tables for the Engle-Granger test of two series with a constant in the cointegrating regression (N = 2, "tau_c"). Every number is read from a source, never fitted here.
/// <para>
/// <b>Critical values</b> (finite sample, <c>c(T) = tauInf + b1/T + b2/T^2</c>): J. G. MacKinnon, "Critical Values for Cointegration Tests", Queen's Economics Department Working Paper No. 1227 (January 2010),
/// Table 2 (working-paper page 13), rows "2 tau_c" at 1, 5 and 10 percent: (-3.89644, -10.9519, -22.527), (-3.33613, -6.1101, -6.823), (-3.04445, -4.2412, -2.720); the b3 term is omitted in the table for N = 2.
/// (statsmodels and arch print -33.527 for the 1 percent b2; the paper prints -22.527; see ADR 0192.)
/// </para>
/// <para>
/// <b>P-values</b> (asymptotic): J. G. MacKinnon, "Approximate asymptotic distribution functions for unit-root and cointegration tests", Journal of Business and Economic Statistics 12(2), 1994, 167-176 (N = 2, constant):
/// <c>p = Phi(g0 + g1 tau + g2 tau^2 [+ g3 tau^3])</c>, the small-p set for <c>tau &lt;= tauStar</c>, the large-p set above; <c>p = 0</c> below <c>tauMin</c> and <c>1</c> above <c>tauMax</c>.
/// The 1994 paper is not freely readable; the coefficients are the published ones as reproduced by two independent libraries (statsmodels <c>adfvalues.py</c> and arch <c>dickey_fuller.py</c>, identical to each other) and are checked against the 2010 critical values
/// (the p-value at each asymptotic critical value must be 0.01, 0.05 and 0.10 within 0.003).
/// </para>
/// </summary>
public static class MacKinnonTables
{
    private const double TauStar = -2.62;
    private const double TauMin = -18.86;
    private const double TauMax = 0.92;

    // Small-p set (tau <= TauStar): the published coefficients (2.92, 1.5012, 3.9796e-2).
    private const double S0 = 2.92;
    private const double S1 = 1.5012;
    private const double S2 = 0.039796;

    // Large-p set (tau > TauStar): the published coefficients (2.1945, 6.4695e-1, -2.9198e-1, -4.2377e-2).
    private const double L0 = 2.1945;
    private const double L1 = 0.64695;
    private const double L2 = -0.29198;
    private const double L3 = -0.042377;

    public static double AsymptoticCritical1 => -3.89644;

    public static double AsymptoticCritical5 => -3.33613;

    public static double AsymptoticCritical10 => -3.04445;

    /// <summary>The critical values at sample size <paramref name="sampleSize"/> (at least 1).</summary>
    public static CriticalValues Critical(int sampleSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleSize, 1);
        double t = sampleSize;
        return new CriticalValues(
            sampleSize,
            -3.89644 + (-10.9519 / t) + (-22.527 / (t * t)),
            -3.33613 + (-6.1101 / t) + (-6.823 / (t * t)),
            -3.04445 + (-4.2412 / t) + (-2.720 / (t * t)));
    }

    /// <summary>
    /// The asymptotic p-value of a statistic; <paramref name="tau"/> must be a number (NaN is a caller bug and gives NaN). The two published fits meet at tauStar with a small gap (the large-p value is 0.0007 below the small-p value there),
    /// so for tau above tauStar the p-value is not allowed below the value at tauStar: this keeps it non-decreasing (ADR 0192).
    /// </summary>
    public static double PValue(double tau)
    {
        if (double.IsNaN(tau))
        {
            return double.NaN;
        }

        if (tau > TauMax)
        {
            return 1.0;
        }

        if (tau < TauMin)
        {
            return 0.0;
        }

        if (tau <= TauStar)
        {
            return NormalDist.Cdf(S0 + (tau * (S1 + (tau * S2))));
        }

        var large = NormalDist.Cdf(L0 + (tau * (L1 + (tau * (L2 + (tau * L3))))));
        var atStar = NormalDist.Cdf(S0 + (TauStar * (S1 + (TauStar * S2))));
        return Math.Max(large, atStar);
    }
}
