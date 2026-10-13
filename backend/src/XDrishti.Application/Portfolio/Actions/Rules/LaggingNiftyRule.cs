using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary>What the lagging rule found: the items, and the holdings it could not judge (held long enough, but without a usable benchmark or XIRR).</summary>
internal sealed record LaggingResult(IReadOnlyList<RuleHit> Hits, IReadOnlyList<HoldingData> Unavailable)
{
    public static LaggingResult None { get; } = new([], []);
}

/// <summary>
/// <b>Lagging Nifty</b>: an open, priced holding held for more than <c>LagDays</c> days (by its oldest open lot) whose XIRR is more than <c>LagPp</c> percentage points below the benchmark's XIRR of the same cash flows
/// (<see cref="BenchmarkReturns"/>: the holding's lots put into the index at its stored closes) is <c>medium</c>. When the benchmark has no bars from the holding's first cash flow, or either XIRR is not <c>ok</c>, the holding is not judged and is
/// reported as unavailable (never counted as lagging or as keeping up). Pure: the index closes and the calendar are given.
/// </summary>
internal static class LaggingNiftyRule
{
    /// <summary>The holdings the rule applies to by age: open, priced, with an open lot, the oldest open lot more than <c>LagDays</c> days old. The caller reads the index bars only when there are any.</summary>
    public static IReadOnlyList<HoldingData> Eligible(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [.. context.Holdings.Where(h => h.IsOpen && h.IsPriced && HeldDays(h, context.Today) is { } days && days > context.Thresholds.LagDays)];
    }

    /// <summary>Days since the oldest open lot was acquired; null without an open lot.</summary>
    public static int? HeldDays(HoldingData holding, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(holding);
        var open = holding.Lots.Where(l => l.IsOpen).Select(l => l.AcquiredOn).ToList();
        return open.Count == 0 ? null : today.DayNumber - open.Min().DayNumber;
    }

    /// <summary><paramref name="indexCloses"/> are the benchmark's adjusted day closes (null or empty: none); <paramref name="calendar"/> the trading calendar (null: none).</summary>
    public static LaggingResult Evaluate(ActionContext context, IReadOnlyList<DailyClose>? indexCloses, TradingCalendar? calendar)
    {
        ArgumentNullException.ThrowIfNull(context);
        var eligible = Eligible(context);
        if (eligible.Count == 0)
        {
            return LaggingResult.None;
        }

        if (indexCloses is not { Count: > 0 } || calendar is null)
        {
            return new LaggingResult([], eligible);
        }

        var t = context.Thresholds;
        var hits = new List<RuleHit>();
        var unavailable = new List<HoldingData>();
        foreach (var holding in eligible)
        {
            var own = context.Returns(holding).Xirr;
            var benchmark = BenchmarkReturns.Compute([holding], context.Today, indexCloses, calendar);
            if (BenchmarkReturns.Alpha(own, benchmark) is not { } gap)
            {
                unavailable.Add(holding);
                continue;
            }

            if (gap >= -t.LagPp)
            {
                continue;
            }

            var days = HeldDays(holding, context.Today) ?? 0;
            var xirr = Math.Round((decimal)((own.Rate ?? 0.0) * 100.0), 4, MidpointRounding.AwayFromZero);
            var bench = BenchmarkReturns.Percent(benchmark) ?? 0m;
            hits.Add(ItemFactory.ForHolding(
                ActionRuleNames.LaggingNifty,
                ActionSeverities.Medium,
                holding,
                "Lagging Nifty",
                $"XIRR {ActionText.Percent(xirr)} against {ActionText.Percent(bench)} in the index on the same cash flows ({ActionText.Percent(-gap)} points behind), held {ActionText.Plural(days, "day", "days")}",
                new ActionNumbers(HeldDays: days, XirrPct: xirr, BenchmarkXirrPct: bench, GapPp: gap, ThresholdPp: t.LagPp),
                -gap - t.LagPp));
        }

        return new LaggingResult(hits, unavailable);
    }
}
