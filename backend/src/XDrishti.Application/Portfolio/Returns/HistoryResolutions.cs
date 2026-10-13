namespace XDrishti.Application.Portfolio.Returns;

/// <summary>The wire names of <see cref="HistoryResolution"/> (<c>day</c>, <c>week</c>, <c>month</c>) and the thinning of a series to one.</summary>
public static class HistoryResolutions
{
    public static string Name(HistoryResolution resolution) => resolution switch
    {
        HistoryResolution.Day => "day",
        HistoryResolution.Week => "week",
        HistoryResolution.Month => "month",
        _ => throw new ArgumentOutOfRangeException(nameof(resolution)),
    };

    public static bool TryParse(string? text, out HistoryResolution resolution)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "day":
                resolution = HistoryResolution.Day;
                return true;
            case "week":
                resolution = HistoryResolution.Week;
                return true;
            case "month":
                resolution = HistoryResolution.Month;
                return true;
            default:
                resolution = default;
                return false;
        }
    }

    /// <summary>
    /// The last point of each period (the points are in date order): of each Monday-to-Sunday week or each calendar month. The very last point is always kept, so the newest figure is never
    /// left out. <see cref="HistoryResolution.Day"/> returns the points unchanged.
    /// </summary>
    public static IReadOnlyList<HistoryPoint> Thin(IReadOnlyList<HistoryPoint> points, HistoryResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (resolution == HistoryResolution.Day || points.Count < 2)
        {
            return points;
        }

        var kept = new List<HistoryPoint>();
        for (var i = 0; i < points.Count; i++)
        {
            if (i == points.Count - 1 || PeriodOf(points[i].Date, resolution) != PeriodOf(points[i + 1].Date, resolution))
            {
                kept.Add(points[i]);
            }
        }

        return kept;
    }

    /// <summary>A number that is the same for every date of one period and grows with time.</summary>
    private static int PeriodOf(DateOnly date, HistoryResolution resolution) => resolution == HistoryResolution.Month
        ? (date.Year * 12) + date.Month
        : date.DayNumber / 7; // day number 0 (0001-01-01) is a Monday, so blocks of seven day numbers are Monday-to-Sunday weeks
}
