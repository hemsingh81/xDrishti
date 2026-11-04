namespace XDrishti.Domain.Confidence;

/// <summary>
/// The moments of a list of trade results in R (CF-03): count, mean, sample standard deviation (n - 1), skewness and kurtosis from the population moments (kurtosis 3 for a normal), and the Sharpe ratio per trade <c>mean / sd</c>. A figure that cannot be computed is NaN:
/// the Sharpe ratio needs 2 trades and a spread, skewness and kurtosis need 4 trades and a spread. Pure.
/// </summary>
public sealed record TradeMoments(int N, double Mean, double Sd, double Skew, double Kurt)
{
    /// <summary>The Sharpe ratio per trade; NaN when there is no spread or fewer than 2 trades.</summary>
    public double Sr => N >= 2 && Sd > 0 && double.IsFinite(Sd) ? Mean / Sd : double.NaN;

    /// <summary>The moments of <paramref name="results"/>; a non-finite result gives NaN throughout.</summary>
    public static TradeMoments Of(IReadOnlyList<double> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var n = results.Count;
        if (n == 0)
        {
            return new TradeMoments(0, double.NaN, double.NaN, double.NaN, double.NaN);
        }

        var sum = 0.0;
        foreach (var v in results)
        {
            sum += v;
        }

        var mean = sum / n;
        if (!double.IsFinite(mean))
        {
            return new TradeMoments(n, double.NaN, double.NaN, double.NaN, double.NaN);
        }

        double m2 = 0, m3 = 0, m4 = 0;
        foreach (var v in results)
        {
            var d = v - mean;
            m2 += d * d;
            m3 += d * d * d;
            m4 += d * d * d * d;
        }

        var sd = n >= 2 ? Math.Sqrt(m2 / (n - 1)) : double.NaN;
        var pop = m2 / n;
        var shape = n >= 4 && pop > 0 && double.IsFinite(pop);
        return new TradeMoments(n, mean, sd, shape ? m3 / n / Math.Pow(pop, 1.5) : double.NaN, shape ? m4 / n / (pop * pop) : double.NaN);
    }
}
