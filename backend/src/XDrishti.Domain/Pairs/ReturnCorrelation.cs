namespace XDrishti.Domain.Pairs;

/// <summary>The Pearson correlation of the daily log returns of two log-price series (<c>r_t = ln p_t - ln p_{t-1}</c>), sums in index order. A flat return series is <c>degenerate_x</c>.</summary>
public static class ReturnCorrelation
{
    public static PairsResult<double> Of(ReadOnlySpan<double> lnY, ReadOnlySpan<double> lnX, PairsOptions? options = null)
    {
        options ??= PairsOptions.Default;
        if (!options.IsValid || !HedgeRatio.AllFinite(lnY) || !HedgeRatio.AllFinite(lnX))
        {
            return PairsResults.Unavailable<double>(PairsReason.BadInput);
        }

        if (lnY.Length != lnX.Length)
        {
            return PairsResults.Unavailable<double>(PairsReason.TooShort);
        }

        var n = lnY.Length;
        if (n < options.MinObservations)
        {
            return PairsResults.Unavailable<double>(PairsReason.TooShort);
        }

        var m = n - 1;
        double my = 0, mx = 0;
        for (var t = 1; t < n; t++)
        {
            my += lnY[t] - lnY[t - 1];
            mx += lnX[t] - lnX[t - 1];
        }

        my /= m;
        mx /= m;
        double sxx = 0, syy = 0, sxy = 0;
        for (var t = 1; t < n; t++)
        {
            var dy = (lnY[t] - lnY[t - 1]) - my;
            var dx = (lnX[t] - lnX[t - 1]) - mx;
            sxx += dx * dx;
            syy += dy * dy;
            sxy += dx * dy;
        }

        if (!(sxx > 1e-24 * m) || !(syy > 1e-24 * m))
        {
            return PairsResults.Unavailable<double>(PairsReason.DegenerateX);
        }

        var r = sxy / Math.Sqrt(sxx * syy);
        return double.IsFinite(r) ? PairsResults.Of(Math.Clamp(r, -1.0, 1.0)) : PairsResults.Unavailable<double>(PairsReason.BadInput);
    }
}
