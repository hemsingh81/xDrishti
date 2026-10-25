namespace XDrishti.Domain.Reporting;

/// <summary>
/// What simply holding a set of instruments would have done (P8-07, ADR 0102): buy each included instrument at its first close on or after the window start, hold to its last close on or before the window end, and take the mean of the price returns
/// (equal weight, no rebalancing, no dividends). An instrument that cannot be taken at face value is left out and named, never repaired. Pure: no I/O, no clock.
/// </summary>
public static class BuyAndHold
{
    /// <summary>No daily bar in the window.</summary>
    public const string NoBars = "no_bars";

    /// <summary>The first bar is more than the allowed gap after the window start.</summary>
    public const string StartsLate = "starts_late";

    /// <summary>The last bar is more than the allowed gap before the window end.</summary>
    public const string EndsEarly = "ends_early";

    /// <summary>A one-session close-to-close move above the threshold (likely an unadjusted corporate action).</summary>
    public const string Discontinuity = "discontinuity";

    /// <summary>A close that is not a positive finite number.</summary>
    public const string BadPrice = "bad_price";

    public static BuyAndHoldResult Compute(IReadOnlyList<InstrumentSeries> series, DateOnly from, DateOnly to, BuyAndHoldOptions options)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(options);
        if (from >= to)
        {
            return new BuyAndHoldResult(Metric.NotEvaluable("the window must start before it ends"), 0, []);
        }

        var returns = new List<double>();
        var excluded = new List<BuyAndHoldExclusion>();
        foreach (var s in series.OrderBy(x => x.Symbol, StringComparer.Ordinal))
        {
            var reason = Evaluate(s, from, to, options, out var ret);
            if (reason is null)
            {
                returns.Add(ret);
            }
            else
            {
                excluded.Add(new BuyAndHoldExclusion(s.Symbol, reason));
            }
        }

        var figure = returns.Count == 0
            ? Metric.Empty("no instrument could be included")
            : returns.Count < options.MinMembers
                ? Metric.Insufficient($"{returns.Count} included, at least {options.MinMembers} are needed")
                : Metric.Of(returns.Order().Sum() / returns.Count * 100.0);
        return new BuyAndHoldResult(figure, returns.Count, excluded);
    }

    private static string? Evaluate(InstrumentSeries s, DateOnly from, DateOnly to, BuyAndHoldOptions options, out double ret)
    {
        ret = 0;
        var bars = s.Closes.Where(c => c.Date >= from && c.Date <= to).OrderBy(c => c.Date).ToList();
        if (bars.Count == 0)
        {
            return NoBars;
        }

        if (bars[0].Date.DayNumber - from.DayNumber > options.MaxStartGapDays)
        {
            return StartsLate;
        }

        if (to.DayNumber - bars[^1].Date.DayNumber > options.MaxStartGapDays)
        {
            return EndsEarly;
        }

        if (bars.Any(b => !double.IsFinite(b.Close) || b.Close <= 0))
        {
            return BadPrice;
        }

        for (var i = 1; i < bars.Count; i++)
        {
            if (Math.Abs(bars[i].Close / bars[i - 1].Close - 1.0) > options.DiscontinuityThreshold)
            {
                return Discontinuity;
            }
        }

        ret = bars[^1].Close / bars[0].Close - 1.0;
        return double.IsFinite(ret) ? null : BadPrice;
    }
}
