using System.Globalization;

namespace XDrishti.Domain.Readiness;

/// <summary>
/// Row 13 of the readiness scorecard (CF-11, owner decision 2026-11-07, ADR 0227): the daily Sharpe of the strategy's net returns against the daily Sharpe of the buy-and-hold of the same universe over the identical days. The strategy beats the benchmark only when its Sharpe is strictly greater.
/// The Sharpe is the mean over the sample standard deviation of the daily returns, not annualised and with no risk-free rate (both series are treated the same way, so the ordering does not depend on either). Too few days, a series that is not a number, or a series with no variance
/// has no Sharpe: the comparison is unavailable, which counts as a fail wherever it is read. Pure.
/// </summary>
/// <param name="Days">The days compared.</param>
/// <param name="StrategySharpe">The daily Sharpe of the strategy; null when it could not be measured.</param>
/// <param name="HoldSharpe">The daily Sharpe of the buy-and-hold; null when it could not be measured.</param>
/// <param name="Reason">Why there is no comparison; empty when both Sharpes are numbers.</param>
public sealed record BenchmarkComparison(int Days, double? StrategySharpe, double? HoldSharpe, string Reason)
{
    /// <summary>Fewest days compared (a proposal: a Sharpe of fewer days is noise).</summary>
    public const int DefaultMinDays = 60;

    /// <summary>A variance at or below this is no variance.</summary>
    private const double Flat = 1e-18;

    /// <summary>True when strictly greater, false when not, null when either Sharpe is missing.</summary>
    public bool? Beaten => Beats(StrategySharpe, HoldSharpe);

    /// <summary>The strategy beats the hold only when its Sharpe is strictly greater; a missing one is no answer.</summary>
    public static bool? Beats(double? strategy, double? hold) => strategy is { } s && hold is { } h && double.IsFinite(s) && double.IsFinite(h) ? s > h : null;

    /// <summary>The comparison of two series over the same days, in the same order.</summary>
    public static BenchmarkComparison Of(IReadOnlyList<double> strategy, IReadOnlyList<double> hold, int minDays = DefaultMinDays)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(hold);
        if (strategy.Count != hold.Count)
        {
            return Unavailable(0, "the strategy and the hold are not over the same days");
        }

        if (strategy.Count < minDays)
        {
            return Unavailable(strategy.Count, string.Create(CultureInfo.InvariantCulture, $"{strategy.Count} days are compared, at least {minDays} are needed"));
        }

        var s = DailySharpe(strategy);
        var h = DailySharpe(hold);
        var why = new List<string>();
        if (s is null)
        {
            why.Add("the strategy's daily returns have no variance or are not numbers");
        }

        if (h is null)
        {
            why.Add("the buy-and-hold's daily returns have no variance or are not numbers");
        }

        return new BenchmarkComparison(strategy.Count, s, h, string.Join("; ", why));
    }

    /// <summary>Mean over sample standard deviation; null with fewer than two returns, a value that is not finite, or no variance.</summary>
    public static double? DailySharpe(IReadOnlyList<double> returns)
    {
        ArgumentNullException.ThrowIfNull(returns);
        if (returns.Count < 2 || returns.Any(r => !double.IsFinite(r)))
        {
            return null;
        }

        var mean = returns.Sum() / returns.Count;
        var variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
        return variance > Flat ? mean / Math.Sqrt(variance) : null;
    }

    /// <summary>
    /// The strategy's value on each day: the sum of the net R of the trades that exited on it, 0 on a day with no exit. R is in units of the risk taken, so the series is a return up to a constant, and a Sharpe does not depend on that constant. Null when a trade exits on a day that is not in
    /// <paramref name="days"/> (it would be left out, so the series would not be the strategy's).
    /// </summary>
    public static IReadOnlyList<double>? StrategyDaily(IReadOnlyList<(DateOnly Exit, double R)> trades, IReadOnlyList<DateOnly> days)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(days);
        var index = new Dictionary<DateOnly, int>(days.Count);
        for (var i = 0; i < days.Count; i++)
        {
            index[days[i]] = i;
        }

        var daily = new double[days.Count];
        foreach (var (exit, r) in trades)
        {
            if (!index.TryGetValue(exit, out var i))
            {
                return null;
            }

            daily[i] += r;
        }

        return daily;
    }

    private static BenchmarkComparison Unavailable(int days, string reason) => new(days, null, null, reason);
}
