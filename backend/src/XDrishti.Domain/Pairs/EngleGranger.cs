namespace XDrishti.Domain.Pairs;

/// <summary>
/// The Engle-Granger residual test (spec P14-12): OLS of <c>ln y</c> on <c>ln x</c> with a constant, then on the residuals <c>Delta e_t = gamma e_{t-1} + sum_{i=1..p} phi_i Delta e_{t-i} + eps_t</c> by OLS without a constant,
/// <c>tau = gamma / se(gamma)</c> with <c>se^2 = s^2 [(X'X)^-1]_00</c> and <c>s^2 = SSR / (m - k)</c> (<c>m = n - 1 - p</c> equations, <c>k = p + 1</c> regressors).
/// The lag <c>p</c> is <see cref="PairsOptions.Lags"/>, fixed and pre-registered. Critical values and the p-value come from <see cref="MacKinnonTables"/>. The test is on the pair as given: it depends on which series is <c>y</c>.
/// </summary>
public static class EngleGranger
{
    /// <summary>Residual sum of squares below this share of the total makes a residual series (or a regression) exact: no statistic is formed.</summary>
    private const double ZeroShare = 1e-20;

    /// <summary>The test on two log-price series.</summary>
    public static PairsResult<EngleGrangerResult> Test(ReadOnlySpan<double> lnY, ReadOnlySpan<double> lnX, PairsOptions? options = null)
    {
        options ??= PairsOptions.Default;
        var p = options.Lags;
        var ols = HedgeRatio.Ols(lnY, lnX, options);
        if (!ols.TryGet(out var fit))
        {
            return PairsResults.Unavailable<EngleGrangerResult>(ols.Reason ?? PairsReason.BadInput);
        }

        if (fit.N < (2 * p) + 3)
        {
            return PairsResults.Unavailable<EngleGrangerResult>(PairsReason.TooShort);
        }

        if (fit.Sse <= ZeroShare * fit.Sst)
        {
            return PairsResults.Unavailable<EngleGrangerResult>(PairsReason.ZeroResidualVariance);
        }

        return Adf(fit, p);
    }

    /// <summary>The test on two price series (any non-finite or non-positive price is <c>bad_input</c>).</summary>
    public static PairsResult<EngleGrangerResult> TestPrices(ReadOnlySpan<double> y, ReadOnlySpan<double> x, PairsOptions? options = null)
    {
        var ly = LogSpread.Ln(y);
        var lx = LogSpread.Ln(x);
        if (!ly.TryGet(out var logY) || !lx.TryGet(out var logX))
        {
            return PairsResults.Unavailable<EngleGrangerResult>(PairsReason.BadInput);
        }

        return Test(logY, logX, options);

    }

    private static PairsResult<EngleGrangerResult> Adf(OlsFit fit, int p)
    {
        var e = fit.Residuals;
        var n = fit.N;
        var m = n - 1 - p;
        var k = p + 1;
        var xtx = new double[k * k];
        var xty = new double[k];
        var row = new double[k];
        double sumDy2 = 0;
        for (var t = p + 1; t < n; t++)
        {
            Fill(row, e, t, p);
            var dy = e[t] - e[t - 1];
            sumDy2 += dy * dy;
            for (var i = 0; i < k; i++)
            {
                xty[i] += row[i] * dy;
                for (var j = 0; j < k; j++)
                {
                    xtx[(i * k) + j] += row[i] * row[j];
                }
            }
        }

        var inv = SmallMatrix.Invert(xtx, k);
        if (inv is null)
        {
            return PairsResults.Unavailable<EngleGrangerResult>(PairsReason.ZeroResidualVariance);
        }

        var coef = new double[k];
        for (var i = 0; i < k; i++)
        {
            double s = 0;
            for (var j = 0; j < k; j++)
            {
                s += inv[(i * k) + j] * xty[j];
            }

            coef[i] = s;
        }

        double ssr = 0;
        for (var t = p + 1; t < n; t++)
        {
            Fill(row, e, t, p);
            var fitted = 0.0;
            for (var i = 0; i < k; i++)
            {
                fitted += coef[i] * row[i];
            }

            var r = (e[t] - e[t - 1]) - fitted;
            ssr += r * r;
        }

        if (!(ssr > ZeroShare * sumDy2) || !(inv[0] > 0))
        {
            return PairsResults.Unavailable<EngleGrangerResult>(PairsReason.ZeroResidualVariance);
        }

        var s2 = ssr / (m - k);
        var se = Math.Sqrt(s2 * inv[0]);
        var tau = coef[0] / se;
        if (!double.IsFinite(tau) || !(se > 0))
        {
            return PairsResults.Unavailable<EngleGrangerResult>(PairsReason.ZeroResidualVariance);
        }

        return PairsResults.Of(new EngleGrangerResult(tau, coef[0], p, n, m, fit, MacKinnonTables.Critical(n - 1), MacKinnonTables.PValue(tau), ssr, se));
    }

    private static void Fill(double[] row, IReadOnlyList<double> e, int t, int p)
    {
        row[0] = e[t - 1];
        for (var i = 1; i <= p; i++)
        {
            row[i] = e[t - i] - e[t - i - 1];
        }
    }
}
