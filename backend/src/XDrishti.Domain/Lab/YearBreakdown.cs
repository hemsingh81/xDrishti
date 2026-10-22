using XDrishti.Domain.Research;

namespace XDrishti.Domain.Lab;

/// <summary>Groups trades by the calendar year of the entry (spec P5-05, assumption 7). Pure.</summary>
public static class YearBreakdown
{
    public static IReadOnlyList<YearRow> Of(IReadOnlyList<SelectionTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        return [.. trades.GroupBy(t => t.Entry.Year).OrderBy(g => g.Key).Select(g =>
        {
            var gains = g.Where(t => t.R > 0).Sum(t => t.R);
            var losses = -g.Where(t => t.R < 0).Sum(t => t.R);
            var total = g.Sum(t => t.R);
            return new YearRow(g.Key, g.Count(), total / g.Count(), total, losses > 0 ? gains / losses : null, total > 0);
        })];
    }
}
