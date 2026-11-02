namespace XDrishti.Domain.Filings;

/// <summary>
/// Reading the result facts at an instant (P15-03, ADR 0184), pure. Only filings visible at <c>t</c> by <see cref="PointInTimeRule.IsVisible"/> are looked at. For each (period end, span, basis) the facts come from the visible filing with the latest broadcast time that carries that
/// span and basis (ties fall to the lower id), so a file of one basis never hides the other basis, and a restatement replaces only what it restates, only from its own time. A filing with no time, or one broadcast later than <c>t</c>, changes nothing; the order of the
/// input changes nothing.
/// </summary>
public static class ResultFactsAsOfRule
{
    public static ResultFactsAnswer Compute(IEnumerable<ResultFilingFacts> rows, DateTimeOffset t, TimeSpan lag)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var all = rows.ToList();
        var undated = all.Count(r => r.Filing.State == BroadcastState.Unavailable || r.Filing.BroadcastTs is null);
        var answers = new List<ResultFactAnswer>();
        var visible = all.Where(r => PointInTimeRule.IsVisible(r.Filing, t, lag) && r.Filing.PeriodEnd is not null).ToList();
        foreach (var key in visible.SelectMany(r => r.Facts.Select(f => (Period: r.Filing.PeriodEnd!.Value, f.Span, f.Basis))).Distinct())
        {
            var source = visible
                .Where(r => r.Filing.PeriodEnd == key.Period && r.Facts.Any(f => f.Span == key.Span && f.Basis == key.Basis))
                .OrderByDescending(r => r.Filing.BroadcastTs!.Value.UtcDateTime)
                .ThenBy(r => r.Filing.Id)
                .First();
            foreach (var fact in source.Facts.Where(f => f.Span == key.Span && f.Basis == key.Basis))
            {
                answers.Add(new ResultFactAnswer(key.Period, fact.Field, fact.Span, fact.Basis, fact.State, fact.Value, fact.Unit, fact.Reason, source.Filing.Id, source.Filing.BroadcastTs));
            }
        }

        var ordered = answers
            .OrderByDescending(a => a.PeriodEnd)
            .ThenBy(a => ResultCodes.Spans.ToList().IndexOf(a.Span))
            .ThenBy(a => ResultCodes.Bases.ToList().IndexOf(a.Basis))
            .ThenBy(a => ResultCodes.Fields.ToList().IndexOf(a.Field))
            .ToList();
        return new ResultFactsAnswer(ordered, undated);
    }
}
