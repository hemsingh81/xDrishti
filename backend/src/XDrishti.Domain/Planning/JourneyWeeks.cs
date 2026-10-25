using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Planning;

/// <summary>ISO-week grouping of graded plan tickets (spec P8-12). Rejected candidates are not tickets of the plan and are left out. The figures are sums and means of stored R.</summary>
public static class JourneyWeeks
{
    /// <summary>A week with fewer graded tickets than this shows its count and no mean.</summary>
    public const int MinGraded = 5;

    /// <summary>The weeks that have at least one ticket, oldest first.</summary>
    public static IReadOnlyList<JourneyWeek> Build(IReadOnlyList<GradedItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return [.. items
            .Where(i => i.Group != TicketGroup.Rejected)
            .GroupBy(i => DigestWeek.Of(i.Date))
            .OrderBy(g => g.Key.Start)
            .Select(g =>
            {
                var r = g.Where(i => i.Grade.HasR).Select(i => i.Grade.R!.Value).ToList();
                var total = r.Sum();
                var enough = r.Count >= MinGraded;
                return new JourneyWeek(g.Key.Key, g.Key.Start, r.Count, total, enough ? total / r.Count : null, enough ? "ok" : "insufficient");
            })];
    }
}
