namespace XDrishti.Domain.Filings;

/// <summary>
/// Turns accepted shareholding rows into groups (P15-04 criterion 7). All rows of a (symbol, period) must carry the same time (same state and same instant to the microsecond), else the whole group is rejected <c>inconsistent_ts</c>. A category that appears twice in a group
/// is rejected on every one of its lines (<c>bad_row</c>), so the outcome never depends on the order of the lines. Groups come out ordered by symbol and period, lines in the order promoter, fii, dii, public.
/// </summary>
public static class ShareholdingGroups
{
    public static (IReadOnlyList<ShareholdingGroup> Groups, IReadOnlyList<RowReject> Rejects) Build(IEnumerable<ShareholdingRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var groups = new List<ShareholdingGroup>();
        var rejects = new List<RowReject>();
        foreach (var group in rows.GroupBy(r => (r.Symbol, r.PeriodEnd)).OrderBy(g => g.Key.Symbol, StringComparer.Ordinal).ThenBy(g => g.Key.PeriodEnd))
        {
            var times = group.Select(r => (r.Broadcast.State, Instant: r.Broadcast.State == BroadcastState.Ok ? NewFiling.ToMicroseconds(r.Broadcast.Instant)?.UtcDateTime : null, r.Broadcast.Reason)).Distinct().ToList();
            if (times.Count > 1)
            {
                rejects.AddRange(group.Select(r => new RowReject(r.Line, CalendarRejectReasons.InconsistentTs, "broadcast_ts")));
                continue;
            }

            var duplicated = group.GroupBy(r => r.Category).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            rejects.AddRange(group.Where(r => duplicated.Contains(r.Category)).Select(r => new RowReject(r.Line, CalendarRejectReasons.BadRow, "category")));
            var lines = ShareholdingCsv.Categories
                .Select(c => group.Where(r => r.Category == c && !duplicated.Contains(c)).Select(r => new ShareholdingLine(c, r.Pct, r.PledgedPct)).FirstOrDefault())
                .Where(l => l is not null).Select(l => l!).ToList();
            if (lines.Count > 0)
            {
                groups.Add(new ShareholdingGroup(group.Key.Symbol, group.Key.PeriodEnd, group.First().Broadcast, lines, ShareholdingGroup.IsSumOff(lines), group.Min(r => r.Line)));
            }
        }

        return (groups, rejects.OrderBy(r => r.Line).ToList());
    }
}
