using System.Globalization;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The day-by-day return of the equal-weight, no-rebalancing hold of <see cref="BuyAndHold"/> (CF-11 row 13, ADR 0227). Which instruments may be used is decided by <see cref="BuyAndHold.Compute"/> itself (the same exclusion rules); here any exclusion, or a figure that is not a number, makes the
/// answer unavailable, because a benchmark with fewer members than the strategy's universe is not the same-universe benchmark. The days are the sessions on which every member has a close; the hold is bought at the last such session before the first asked day and its value on a day is the mean of
/// close over purchase close, so the weights drift with the prices (no rebalancing). Price return, dividends not included. Pure.
/// </summary>
public static class BuyAndHoldDaily
{
    /// <param name="series">The members of the universe with their daily closes; read from before <paramref name="from"/>.</param>
    /// <param name="from">The start of the window the inclusion rules look at; it must precede <paramref name="firstDay"/> by the purchase session.</param>
    /// <param name="to">The last day of the window.</param>
    /// <param name="firstDay">The first day a return is asked for.</param>
    /// <param name="options">The rules of <see cref="BuyAndHold"/>.</param>
    public static BuyAndHoldDailyResult Compute(IReadOnlyList<InstrumentSeries> series, DateOnly from, DateOnly to, DateOnly firstDay, BuyAndHoldOptions options)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(options);
        var whole = BuyAndHold.Compute(series, from, to, options);
        if (whole.Excluded.Count > 0)
        {
            var first = whole.Excluded[0];
            return Fail(string.Create(CultureInfo.InvariantCulture, $"{whole.Excluded.Count} of {series.Count} members cannot be used for the buy-and-hold (the first is {first.Symbol}: {first.Reason}), so it is not the same-universe benchmark"));
        }

        if (whole.Return.Number is null)
        {
            return Fail("the buy-and-hold cannot be computed: " + (whole.Return.Reason ?? "no figure"));
        }

        var byDate = new List<Dictionary<DateOnly, double>>(series.Count);
        foreach (var s in series)
        {
            var closes = new Dictionary<DateOnly, double>();
            foreach (var c in s.Closes.Where(c => c.Date >= from && c.Date <= to))
            {
                if (!closes.TryAdd(c.Date, c.Close))
                {
                    return Fail($"{s.Symbol} has two daily closes on {c.Date:yyyy-MM-dd}");
                }
            }

            byDate.Add(closes);
        }

        var common = byDate[0].Keys.Where(d => byDate.TrueForAll(m => m.ContainsKey(d))).Order().ToList();
        var start = common.FindIndex(d => d >= firstDay);
        if (start < 1)
        {
            return Fail(start < 0 ? "no session on which every member has a close in the window" : "no session before the first day on which every member has a close, so there is no purchase close");
        }

        var days = new List<DateOnly>();
        var returns = new List<double>();
        var purchase = common[start - 1];
        var previous = 1.0;
        for (var k = start; k < common.Count; k++)
        {
            var value = byDate.Sum(m => m[common[k]] / m[purchase]) / byDate.Count;
            var ret = value / previous - 1.0;
            if (!double.IsFinite(ret))
            {
                return Fail("a daily return is not a finite number");
            }

            days.Add(common[k]);
            returns.Add(ret);
            previous = value;
        }

        return new BuyAndHoldDailyResult(days, returns, string.Empty);
    }

    private static BuyAndHoldDailyResult Fail(string reason) => new([], [], reason);
}
