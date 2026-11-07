using XDrishti.Domain.Research;

namespace XDrishti.Domain.Lab;

/// <summary>
/// Groups trades by the calendar quarter of the entry (SC-S1, ADR 0222). Only a quarter with at least one trade is a row: a quarter with none is not a quarter at or below the floor, it is no observation. The floor is a parameter, so this type names nothing of the
/// readiness slice; the scorecard reader passes its constant. Pure.
/// </summary>
public static class QuarterBreakdown
{
    /// <summary>The mean is rounded to this many places before it is compared with the floor, so a mean of exactly the floor is not lost to the last bit of a sum.</summary>
    private const int ComparePlaces = 9;

    public static IReadOnlyList<QuarterRow> Of(IReadOnlyList<SelectionTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        return [.. trades
            .GroupBy(t => (t.Entry.Year, Quarter: ((t.Entry.Month - 1) / 3) + 1))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Quarter)
            .Select(g => new QuarterRow(g.Key.Year, g.Key.Quarter, g.Count(), g.Sum(t => t.R) / g.Count()))];
    }

    /// <summary>The share of the quarters whose mean R is at or above <paramref name="floorR"/> (a mean of exactly the floor counts); null with no quarter.</summary>
    public static double? Share(IReadOnlyList<double> means, double floorR)
    {
        ArgumentNullException.ThrowIfNull(means);
        return means.Count == 0 ? null : (double)means.Count(m => Math.Round(m, ComparePlaces) >= floorR) / means.Count;
    }
}
