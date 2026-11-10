namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>
/// The volatility scale (spec BK-10): the EWMA volatility of a share against its own usual level. Pure. The scale is <c>clamp(target / sigma, Floor, 1.0)</c>: it can only reduce a quantity, and the 1.0 is a constant of this type, not a setting.
/// </summary>
public static class VolTarget
{
    /// <summary>The scale is never above this, whatever the inputs.</summary>
    public const double Ceiling = 1.0;

    /// <summary>One step of the EWMA variance: <c>lambda * variance + (1 - lambda) * r^2</c>.</summary>
    public static double NextVariance(double variance, double dailyReturn, double lambda) => (lambda * variance) + ((1 - lambda) * dailyReturn * dailyReturn);

    /// <summary>The variance after each return, starting from <paramref name="seedVariance"/> (the first entry of the list is the seed itself).</summary>
    public static IReadOnlyList<double> Evolve(double seedVariance, IEnumerable<double> returns, double lambda)
    {
        ArgumentNullException.ThrowIfNull(returns);
        var list = new List<double> { seedVariance };
        var v = seedVariance;
        foreach (var r in returns)
        {
            v = NextVariance(v, r, lambda);
            list.Add(v);
        }

        return list;
    }

    /// <summary>
    /// <c>clamp(target / sigma, floor, 1.0)</c>. A sigma of 0 (nothing moved) gives 1.0. Null when the target or sigma is not a usable number. A floor outside 0.25 to 1.0 is pulled into it, so the result is always in [0.25, 1.0].
    /// </summary>
    public static double? Scale(double target, double sigma, double floor)
    {
        if (!double.IsFinite(target) || !double.IsFinite(sigma) || target <= 0 || sigma < 0)
        {
            return null;
        }

        var low = double.IsFinite(floor) ? Math.Clamp(floor, SizingOptions.LowestFloor, Ceiling) : SizingOptions.LowestFloor;
        return sigma == 0 ? Ceiling : Math.Clamp(target / sigma, low, Ceiling);
    }

    /// <summary>
    /// The reading from the share's close-to-close returns up to the previous close (oldest first; a return that is not a finite number is dropped). Variance is seeded with the sample variance of the first 20 returns, then updated by each later return.
    /// Fewer than <see cref="SizingOptions.MinReturns"/> returns: <c>insufficient_returns</c>. The target is the median volatility of the 252 values before today's; fewer than <see cref="SizingOptions.MinHistory"/>: <c>insufficient_history</c>.
    /// </summary>
    public static VolReading Read(IReadOnlyList<double> returns, SizingOptions options)
    {
        ArgumentNullException.ThrowIfNull(returns);
        ArgumentNullException.ThrowIfNull(options);
        var usable = returns.Where(double.IsFinite).ToList();
        if (usable.Count < Math.Max(options.MinReturns, SizingOptions.SeedReturns))
        {
            return new VolReading("insufficient_returns", usable.Count, null, null, null);
        }

        var seed = SampleVariance(usable.Take(SizingOptions.SeedReturns));
        var sigmas = Evolve(seed, usable.Skip(SizingOptions.SeedReturns), options.Lambda).Select(Math.Sqrt).ToList();
        var sigma = sigmas[^1];
        var window = sigmas.Take(sigmas.Count - 1).TakeLast(SizingOptions.TargetSessions).Order().ToList();
        if (window.Count < options.MinHistory)
        {
            return new VolReading("insufficient_history", usable.Count, sigma, null, null);
        }

        var target = Median(window);
        return new VolReading(VolReading.Ok, usable.Count, sigma, target, Scale(target, sigma, options.Floor));
    }

    private static double SampleVariance(IEnumerable<double> values)
    {
        var list = values.ToList();
        var mean = list.Average();
        return list.Sum(x => (x - mean) * (x - mean)) / (list.Count - 1);
    }

    private static double Median(List<double> sorted) => sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
}
