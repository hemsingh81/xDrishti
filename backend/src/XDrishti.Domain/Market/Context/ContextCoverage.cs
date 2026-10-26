namespace XDrishti.Domain.Market.Context;

/// <summary>
/// What the stored days make readable (P3-01d): how many days and from when to when, the first date each family of context values can be read (the day after the Nth record, because a read is of the latest day strictly before the
/// date asked), how many days have a VIX, the calendar span of the last window of VIX days (D1) and the first date on which the regime has both its parts (D2). Dates are null while the history is too short.
/// </summary>
public sealed record ContextCoverage(
    int Days,
    DateOnly? First,
    DateOnly? Last,
    DateOnly? IndexTrendReadableFrom,
    DateOnly? RegimeReadableFrom,
    DateOnly? VixPctReadableFrom,
    DateOnly? RegimeWithVixReadableFrom,
    int VixDays,
    int VixMissingDays,
    int? VixWindowSpanDays,
    bool VixWindowTooWide)
{
    /// <summary>Records before the index trend can be read (50-day average, its slope over 5 days, the ADX).</summary>
    public const int IndexTrendDays = 55;

    /// <summary>Records before the regime can be read (the index volatility percentile over 252 days of a 14-day ATR).</summary>
    public const int RegimeDays = 265;

    /// <summary>VIX days before the VIX percentile can be read.</summary>
    public const int VixWindowDays = 252;

    /// <summary>The calendar span of the VIX window above which the coverage warns.</summary>
    public const int WideWindowDays = 400;

    public static ContextCoverage Of(IReadOnlyList<ContextDayRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ordered = records.OrderBy(r => r.Date).ToList();
        var vixDates = ordered.Where(r => r.VixClose is not null).Select(r => r.Date).ToList();
        int? span = vixDates.Count >= VixWindowDays ? vixDates[^1].DayNumber - vixDates[^VixWindowDays].DayNumber : null;

        DateOnly? withVix = null;
        var vixSeen = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].VixClose is not null)
            {
                vixSeen++;
            }

            if (i + 1 >= RegimeDays && vixSeen >= VixWindowDays && ordered[i].VixClose is not null)
            {
                withVix = ordered[i].Date.AddDays(1);
                break;
            }
        }

        return new ContextCoverage(
            ordered.Count,
            ordered.Count > 0 ? ordered[0].Date : null,
            ordered.Count > 0 ? ordered[^1].Date : null,
            After(ordered, IndexTrendDays),
            After(ordered, RegimeDays),
            vixDates.Count >= VixWindowDays ? vixDates[VixWindowDays - 1].AddDays(1) : null,
            withVix,
            vixDates.Count,
            ordered.Count - vixDates.Count,
            span,
            span is > WideWindowDays);
    }

    private static DateOnly? After(List<ContextDayRecord> ordered, int count) => ordered.Count >= count ? ordered[count - 1].Date.AddDays(1) : null;
}
