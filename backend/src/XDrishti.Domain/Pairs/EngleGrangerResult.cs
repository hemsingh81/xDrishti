namespace XDrishti.Domain.Pairs;

/// <summary>
/// An Engle-Granger test: the statistic <c>tau = gamma / se(gamma)</c>, the lags used, the series length <c>N</c> (the critical values are taken at <c>T = N - 1</c>, the observations of the unit-root regression, MacKinnon 2010 section 3), the number of equations of the test regression, the hedge fit (so the spread is at hand),
/// the sum of squared residuals and the standard error of gamma of the test regression, the finite-sample critical values and the asymptotic p-value (always the asymptotic one, labelled <see cref="PairsLabels.PValueAsymptotic"/> by the caller).
/// </summary>
public sealed record EngleGrangerResult(double Tau, double Gamma, int LagsUsed, int N, int Equations, OlsFit Hedge, CriticalValues Critical, double PValue, double Ssr, double SeGamma)
{
    /// <summary>True when tau is below the finite-sample 5 percent critical value.</summary>
    public bool RejectsAt5Percent => Tau < Critical.Percent5;
}
