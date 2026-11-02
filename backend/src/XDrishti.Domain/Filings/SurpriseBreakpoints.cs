namespace XDrishti.Domain.Filings;

/// <summary>
/// The quintile breakpoints of a surprise measure (P15-09, pure): the 20, 40, 60 and 80 % quantiles of the values of one season by the nearest-rank rule (the value at rank <c>ceil(p * n / 100)</c> of the ascending values), and the quintile 1 to 5 a value falls in. A value equal to a
/// breakpoint goes to the higher quintile (ties go up). Fewer than <see cref="MinimumValues"/> finite values give NaN breakpoints for the season, and a NaN breakpoint gives a NaN quintile: a distribution that thin ranks nothing.
/// </summary>
public static class SurpriseBreakpoints
{
    /// <summary>The least number of finite values a season needs before it ranks anything.</summary>
    public const int MinimumValues = 100;

    /// <summary>The number of breakpoints (four cut a distribution into five quintiles).</summary>
    public const int Count = 4;

    private static readonly int[] Percents = [20, 40, 60, 80];

    /// <summary>The four breakpoints of the finite values, or four NaN when there are fewer than <see cref="MinimumValues"/>.</summary>
    public static double[] Of(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Where(double.IsFinite).Order().ToArray();
        var result = new double[Count];
        if (sorted.Length < MinimumValues)
        {
            Array.Fill(result, double.NaN);
            return result;
        }

        for (var i = 0; i < Count; i++)
        {
            var rank = ((Percents[i] * sorted.Length) + 99) / 100;   // ceil(p * n / 100) in integers: no floating-point edge at an exact multiple
            result[i] = sorted[rank - 1];
        }

        return result;
    }

    /// <summary>The quintile 1 to 5 of the value (1 plus the number of breakpoints at or below it), or NaN when the value or a breakpoint is not a number.</summary>
    public static double QuintileOf(double value, IReadOnlyList<double> breakpoints)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);
        if (!double.IsFinite(value) || breakpoints.Count != Count || breakpoints.Any(b => !double.IsFinite(b)))
        {
            return double.NaN;
        }

        var quintile = 1;
        foreach (var breakpoint in breakpoints)
        {
            if (value >= breakpoint)
            {
                quintile++;
            }
        }

        return quintile;
    }
}
