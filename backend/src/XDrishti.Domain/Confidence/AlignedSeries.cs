namespace XDrishti.Domain.Confidence;

/// <summary>
/// The per-variant daily series of CF-03 part B, aligned on the union of the days on which any variant had an exit: the day's net R summed per variant (0 on a day with no exit of that variant), rounded to <see cref="Decimals"/> places so the stored
/// document stays small and PBO is computed from exactly what is stored. A value that is not finite makes the whole set unavailable (<c>unavailable: &lt;reason&gt;</c>), never a guess.
/// </summary>
/// <param name="Dates">The days in order, each once.</param>
/// <param name="Values">One array per variant, in the order given, each as long as <paramref name="Dates"/>.</param>
/// <param name="Unavailable">The text <c>unavailable: &lt;reason&gt;</c>; null when the series are usable.</param>
public sealed record AlignedSeries(IReadOnlyList<DateOnly> Dates, IReadOnlyList<double[]> Values, string? Unavailable)
{
    /// <summary>The decimals a stored daily value keeps.</summary>
    public const int Decimals = 4;

    /// <summary>Aligns the (exit date, net R) pairs of each variant.</summary>
    // shortcut: the blocks of Cscv count trade days (days on which some variant exited), not sessions, so a quiet stretch weighs less than a busy one. Align on the session calendar if that is needed later.
    public static AlignedSeries Align(IReadOnlyList<IReadOnlyList<(DateOnly Date, double R)>> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);
        var perVariant = new List<Dictionary<DateOnly, double>>(variants.Count);
        foreach (var trades in variants)
        {
            var byDay = new Dictionary<DateOnly, double>();
            foreach (var (date, r) in trades)
            {
                if (!double.IsFinite(r))
                {
                    return new AlignedSeries([], [], "unavailable: a daily value is not a finite number");
                }

                byDay[date] = byDay.GetValueOrDefault(date) + r;
            }

            perVariant.Add(byDay);
        }

        var dates = perVariant.SelectMany(d => d.Keys).Distinct().Order().ToArray();
        var values = new List<double[]>(variants.Count);
        foreach (var byDay in perVariant)
        {
            var row = new double[dates.Length];
            for (var i = 0; i < dates.Length; i++)
            {
                var v = byDay.GetValueOrDefault(dates[i]);
                if (!double.IsFinite(v))
                {
                    return new AlignedSeries([], [], "unavailable: a daily value is not a finite number");
                }

                row[i] = Math.Round(v, Decimals) + 0.0; // + 0.0 turns a negative zero into zero
            }

            values.Add(row);
        }

        return new AlignedSeries(dates, values, null);
    }
}
