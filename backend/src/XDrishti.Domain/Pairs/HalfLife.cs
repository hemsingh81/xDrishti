namespace XDrishti.Domain.Pairs;

/// <summary>
/// The half-life of a spread in bars: <c>Delta s_t = a + b s_{t-1} + eps</c> by OLS, <c>HL = -ln 2 / ln(1 + b)</c>. <c>b &gt;= 0</c> (or so close to 0 that the half-life exceeds 1e11 bars) is <c>not_mean_reverting</c>; <c>1 + b &lt;= 0</c> is <c>oscillating</c>.
/// </summary>
public static class HalfLife
{
    private const double NoReversionBelow = 1e-12;

    public static PairsResult<double> Of(ReadOnlySpan<double> spread, PairsOptions? options = null)
    {
        options ??= PairsOptions.Default;
        if (!options.IsValid || !HedgeRatio.AllFinite(spread))
        {
            return PairsResults.Unavailable<double>(PairsReason.BadInput);
        }

        var n = spread.Length;
        if (n < options.MinObservations)
        {
            return PairsResults.Unavailable<double>(PairsReason.TooShort);
        }

        var m = n - 1;
        double meanLag = 0, meanDiff = 0;
        for (var t = 1; t < n; t++)
        {
            meanLag += spread[t - 1];
            meanDiff += spread[t] - spread[t - 1];
        }

        meanLag /= m;
        meanDiff /= m;
        double sxx = 0, sxy = 0;
        for (var t = 1; t < n; t++)
        {
            var dx = spread[t - 1] - meanLag;
            var dy = (spread[t] - spread[t - 1]) - meanDiff;
            sxx += dx * dx;
            sxy += dx * dy;
        }

        if (!(sxx > 1e-14 * m))
        {
            return PairsResults.Unavailable<double>(PairsReason.DegenerateX);
        }

        var b = sxy / sxx;
        if (!double.IsFinite(b))
        {
            return PairsResults.Unavailable<double>(PairsReason.BadInput);
        }

        if (b >= -NoReversionBelow)
        {
            return PairsResults.Unavailable<double>(PairsReason.NotMeanReverting);
        }

        if (1.0 + b <= 0)
        {
            return PairsResults.Unavailable<double>(PairsReason.Oscillating);
        }

        var hl = -Math.Log(2.0) / Math.Log(1.0 + b);
        return double.IsFinite(hl) && hl > 0 ? PairsResults.Of(hl) : PairsResults.Unavailable<double>(PairsReason.NotMeanReverting);
    }

    /// <summary>True when the half-life, converted by the caller from bars to sessions, lies in the pre-registered range.</summary>
    public static bool InRange(double sessions, PairsOptions? options = null)
    {
        options ??= PairsOptions.Default;
        return double.IsFinite(sessions) && sessions >= options.HalfLifeMinSessions && sessions <= options.HalfLifeMaxSessions;
    }
}
