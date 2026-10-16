namespace XDrishti.Indicators.Tests.Support;

/// <summary>Reads back what a <see cref="MarketContext"/> holds for each day, and compares two readings.</summary>
internal static class ContextRead
{
    /// <summary>The names of the ten value fields, in the order of <see cref="Fields"/>.</summary>
    public static readonly string[] FieldNames =
    [
        nameof(ContextValues.IndexTrend), nameof(ContextValues.IndexAdx), nameof(ContextValues.IndexVolPct), nameof(ContextValues.Vix), nameof(ContextValues.VixPct),
        nameof(ContextValues.VixChg), nameof(ContextValues.Breadth), nameof(ContextValues.RegimeTrend), nameof(ContextValues.RegimeVol), nameof(ContextValues.Regime),
    ];

    /// <summary>The values of each given day as the context holds them: the answer of <c>TryAsOf</c> for the day after, which is the latest day strictly before it, that is the day itself.</summary>
    public static List<ContextValues> Days(MarketContext context, IReadOnlyList<ContextDay> days)
    {
        var values = new List<ContextValues>(days.Count);
        foreach (var day in days)
        {
            context.TryAsOf(day.Date.AddDays(1), out var read).ShouldBeTrue($"{day.Date} is in the context");
            read.Date.ShouldBe(day.Date);
            values.Add(read);
        }

        return values;
    }

    public static double[] Fields(ContextValues v) =>
        [v.IndexTrend, v.IndexAdx, v.IndexVolPct, v.Vix, v.VixPct, v.VixChg, v.Breadth, v.RegimeTrend, v.RegimeVol, v.Regime];

    /// <summary>One column of the reference per field, in the order of <see cref="FieldNames"/>.</summary>
    public static double[][] Columns(ReferenceContextValues r) =>
        [r.IndexTrend, r.IndexAdx, r.IndexVolPct, r.Vix, r.VixPct, r.VixChg, r.Breadth, r.RegimeTrend, r.RegimeVol, r.Regime];

    /// <summary>The same bits in every field (NaN is NaN, +0 is not −0): the prefix property and determinism demand exactness, not a tolerance.</summary>
    public static bool Identical(ContextValues a, ContextValues b) =>
        a.Date == b.Date && Fields(a).Zip(Fields(b)).All(p => Tolerance.Identical(p.First, p.Second));

    /// <summary>The first field in which two readings differ, for the failure message, or null when they are identical.</summary>
    public static string? FirstDifference(ContextValues a, ContextValues b)
    {
        if (a.Date != b.Date)
        {
            return $"date {a.Date} vs {b.Date}";
        }

        var x = Fields(a);
        var y = Fields(b);
        for (var i = 0; i < x.Length; i++)
        {
            if (!Tolerance.Identical(x[i], y[i]))
            {
                return $"{FieldNames[i]} {x[i]:R} vs {y[i]:R}";
            }
        }

        return null;
    }
}
