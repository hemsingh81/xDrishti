namespace XDrishti.Domain.Filings;

/// <summary>The shareholding as known at t (P15-04 criterion 7, pure): the point-in-time rule picks the current group per period end; the newest period is current and the one exactly one quarter earlier (by period end) is the previous quarter's, else none. None visible is <c>unavailable/no_entry</c>.</summary>
public static class ShareholdingAsOfRule
{
    public static ShareholdingAnswer Compute(IEnumerable<ShareholdingEnvelope> envelopes, DateTimeOffset t, TimeSpan lag)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        var byId = envelopes.ToDictionary(e => e.Filing.Id);
        var current = PointInTimeRule.AsOf(byId.Values.Select(e => e.Filing), t, lag).Filings.Where(f => f.PeriodEnd is not null).Select(f => byId[f.Id]).ToList();
        if (current.Count == 0)
        {
            return new ShareholdingAnswer(ShareholdingAnswer.Unavailable, null, null, ShareholdingAnswer.NoEntry);
        }

        return new ShareholdingAnswer(ShareholdingAnswer.Value, Snapshot(current[0]), Snapshot(current.FirstOrDefault(e => e.Filing.PeriodEnd == PreviousQuarterEnd(current[0].Filing.PeriodEnd!.Value))), null);
    }

    private static DateOnly PreviousQuarterEnd(DateOnly period)
    {
        var earlier = period.AddMonths(-3);
        return new DateOnly(earlier.Year, earlier.Month, DateTime.DaysInMonth(earlier.Year, earlier.Month));
    }

    private static ShareholdingSnapshot? Snapshot(ShareholdingEnvelope? e) =>
        e is null ? null :
        new(e.Filing.PeriodEnd!.Value, e.Filing.BroadcastTs!.Value, e.Lines, ShareholdingGroup.IsSumOff(e.Lines));
}
