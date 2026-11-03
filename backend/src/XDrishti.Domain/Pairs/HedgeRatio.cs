namespace XDrishti.Domain.Pairs;

/// <summary>
/// The hedge ratio by ordinary least squares, sums in index order (canonical): <c>beta = sum((x - xbar)(y - ybar)) / sum((x - xbar)^2)</c>, <c>alpha = ybar - beta xbar</c>, <c>e_t = y_t - alpha - beta x_t</c>.
/// Inputs are natural logs of prices (see <see cref="LogSpread.Ln"/>).
/// </summary>
public static class HedgeRatio
{
    /// <summary>The fit; <c>bad_input</c> for a non-finite number, <c>too_short</c> for different lengths (spec Inputs rule) or below <c>MinObservations</c>, <c>degenerate_x</c> when <c>sum((x - xbar)^2) &lt;= 1e-14 n</c>.</summary>
    public static PairsResult<OlsFit> Ols(ReadOnlySpan<double> lnY, ReadOnlySpan<double> lnX, PairsOptions? options = null)
    {
        options ??= PairsOptions.Default;
        if (!options.IsValid || !AllFinite(lnY) || !AllFinite(lnX))
        {
            return PairsResults.Unavailable<OlsFit>(PairsReason.BadInput);
        }

        if (lnY.Length != lnX.Length)
        {
            return PairsResults.Unavailable<OlsFit>(PairsReason.TooShort);
        }

        var n = lnY.Length;
        if (n < options.MinObservations)
        {
            return PairsResults.Unavailable<OlsFit>(PairsReason.TooShort);
        }

        var xbar = Mean(lnX);
        var ybar = Mean(lnY);
        double sxx = 0, sxy = 0, syy = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = lnX[i] - xbar;
            var dy = lnY[i] - ybar;
            sxx += dx * dx;
            sxy += dx * dy;
            syy += dy * dy;
        }

        if (!(sxx > 1e-14 * n))
        {
            return PairsResults.Unavailable<OlsFit>(PairsReason.DegenerateX);
        }

        var beta = sxy / sxx;
        var alpha = ybar - (beta * xbar);
        var residuals = new double[n];
        double sse = 0;
        for (var i = 0; i < n; i++)
        {
            var e = lnY[i] - alpha - (beta * lnX[i]);
            residuals[i] = e;
            sse += e * e;
        }

        var r2 = syy > 0 ? 1.0 - (sse / syy) : 1.0;
        return double.IsFinite(beta) && double.IsFinite(alpha) && double.IsFinite(sse)
            ? PairsResults.Of(new OlsFit(alpha, beta, residuals, r2, n, sse, syy))
            : PairsResults.Unavailable<OlsFit>(PairsReason.BadInput);
    }

    internal static bool AllFinite(ReadOnlySpan<double> values)
    {
        foreach (var v in values)
        {
            if (!double.IsFinite(v))
            {
                return false;
            }
        }

        return true;
    }

    internal static double Mean(ReadOnlySpan<double> values)
    {
        double sum = 0;
        foreach (var v in values)
        {
            sum += v;
        }

        return sum / values.Length;
    }
}
