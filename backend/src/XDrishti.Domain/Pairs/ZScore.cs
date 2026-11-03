namespace XDrishti.Domain.Pairs;

/// <summary>
/// <c>z = (s - mean(W)) / sd(W)</c> over a window <c>W</c> of earlier spread values only (sample sd, <c>n - 1</c>, two passes in index order). Fewer than <c>MinWindow</c> values is <c>short_window</c>; <c>sd &lt;= 1e-12</c> is <c>flat_window</c>.
/// </summary>
public static class ZScore
{
    public static PairsResult<double> Of(ReadOnlySpan<double> window, double value, PairsOptions? options = null)
    {
        options ??= PairsOptions.Default;
        if (!options.IsValid || !double.IsFinite(value) || !HedgeRatio.AllFinite(window))
        {
            return PairsResults.Unavailable<double>(PairsReason.BadInput);
        }

        if (window.Length < options.MinWindow)
        {
            return PairsResults.Unavailable<double>(PairsReason.ShortWindow);
        }

        var mean = HedgeRatio.Mean(window);
        double ss = 0;
        foreach (var v in window)
        {
            var d = v - mean;
            ss += d * d;
        }

        var sd = Math.Sqrt(ss / (window.Length - 1));
        if (!(sd > 1e-12))
        {
            return PairsResults.Unavailable<double>(PairsReason.FlatWindow);
        }

        var z = (value - mean) / sd;
        return double.IsFinite(z) ? PairsResults.Of(z) : PairsResults.Unavailable<double>(PairsReason.BadInput);
    }
}
