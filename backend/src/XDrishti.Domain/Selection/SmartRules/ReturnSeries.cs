namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>Daily close-to-close returns from daily closes (spec P7-09). Pure; used to feed <see cref="CorrelationTable.FromReturns"/>.</summary>
public static class ReturnSeries
{
    /// <summary>
    /// Returns of consecutive sessions. A close after <paramref name="lastSession"/> is dropped first (no look-ahead: nothing after the close of the session counts), a close that is not a positive finite number is dropped,
    /// two closes for one date make that date ambiguous and drop it, and the rest are taken in date order. The return of a date is its close over the previous kept close, minus 1.
    /// </summary>
    public static IReadOnlyList<DailyReturn> FromCloses(IEnumerable<(DateOnly Date, double Close)> closes, DateOnly lastSession)
    {
        ArgumentNullException.ThrowIfNull(closes);
        var kept = closes
            .Where(c => c.Date <= lastSession && double.IsFinite(c.Close) && c.Close > 0)
            .GroupBy(c => c.Date)
            .Where(g => g.Select(c => c.Close).Distinct().Count() == 1)
            .Select(g => (Date: g.Key, Close: g.First().Close))
            .OrderBy(c => c.Date)
            .ToList();
        var result = new List<DailyReturn>(Math.Max(0, kept.Count - 1));
        for (var i = 1; i < kept.Count; i++)
        {
            result.Add(new DailyReturn(kept[i].Date, kept[i].Close / kept[i - 1].Close - 1));
        }

        return result;
    }
}
