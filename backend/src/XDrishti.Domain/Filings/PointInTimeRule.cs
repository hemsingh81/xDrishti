namespace XDrishti.Domain.Filings;

/// <summary>
/// The point-in-time rule (D3, P15-02), pure. A filing is <b>visible at t</b> iff its broadcast time is known and <c>broadcast + lag &lt;= t</c> (inclusive); never the ingest time, the period end or a date. Of the visible filings of one period the latest by broadcast
/// time is <b>current</b> (a restatement hides its predecessor only from its own time on; a filing broadcast later than t, or with no time, changes nothing). Filings with no period stand alone. Ties (which ingest refuses) fall to the lower id so the answer never depends on the order of the rows.
/// </summary>
public static class PointInTimeRule
{
    public const int MinLagMinutes = 0;

    public const int MaxLagMinutes = 60;

    public static bool IsVisible(StoredFiling filing, DateTimeOffset t, TimeSpan lag)
    {
        ArgumentNullException.ThrowIfNull(filing);
        return filing.State == BroadcastState.Ok && filing.BroadcastTs is { } ts && ts + lag <= t;
    }

    /// <summary>The answer at <paramref name="t"/> from the filings of one instrument and kind (any order, future and undated ones included or not: they cannot change it).</summary>
    public static AsOfResult AsOf(IEnumerable<StoredFiling> rows, DateTimeOffset t, TimeSpan lag)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var all = rows.ToList();
        var unavailable = all.Count(f => f.State == BroadcastState.Unavailable || f.BroadcastTs is null);
        var visible = all.Where(f => IsVisible(f, t, lag)).ToList();
        var superseded = visible.Where(f => f.Supersedes is not null).Select(f => f.Supersedes!.Value).ToHashSet();
        var current = new List<StoredFiling>();
        foreach (var group in visible.Where(f => f.PeriodEnd is not null).GroupBy(f => f.PeriodEnd))
        {
            var latest = group.Where(f => !superseded.Contains(f.Id)).OrderByDescending(f => f.BroadcastTs!.Value.UtcDateTime).ThenBy(f => f.Id).FirstOrDefault();
            if (latest is not null)
            {
                current.Add(latest);
            }
        }

        current.AddRange(visible.Where(f => f.PeriodEnd is null && !superseded.Contains(f.Id)));
        var ordered = current.OrderByDescending(f => f.PeriodEnd.HasValue).ThenByDescending(f => f.PeriodEnd).ThenBy(f => f.Id).ToList();
        var state = ordered.Count > 0 ? AsOfResult.Value : unavailable > 0 ? AsOfResult.Unavailable : AsOfResult.NoneVisible;
        return new AsOfResult(state, ordered, unavailable);
    }

    /// <summary>Whether <paramref name="candidate"/> may supersede <paramref name="superseded"/>: same instrument, kind and period, both with a known time, strictly later.</summary>
    public static bool CanSupersede(StoredFiling superseded, NewFiling candidate)
    {
        ArgumentNullException.ThrowIfNull(superseded);
        ArgumentNullException.ThrowIfNull(candidate);
        return superseded.State == BroadcastState.Ok && candidate.State == BroadcastState.Ok
            && superseded.InstrumentId == candidate.InstrumentId && superseded.Kind == candidate.Kind && superseded.PeriodEnd is not null && superseded.PeriodEnd == candidate.PeriodEnd
            && superseded.BroadcastTs is { } before && candidate.BroadcastTs is { } after && after.UtcDateTime > before.UtcDateTime;
    }

    /// <summary>
    /// How a new file relates to the stored filings of its (instrument, kind, period) <paramref name="group"/>. A file with no time, no period or an empty group stands alone; a time equal to the time of ANY dated filing of the group (other bytes) is ambiguous and not
    /// stored; a time later than the latest is a revision of that latest filing; an earlier time is stored unlinked and flagged out of order (visible by its own time, hides nothing later).
    /// </summary>
    public static RevisionDecision Decide(NewFiling candidate, IReadOnlyCollection<StoredFiling> group)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(group);
        var dated = group.Where(f => f.State == BroadcastState.Ok && f.BroadcastTs is not null).ToList();
        if (candidate.State != BroadcastState.Ok || candidate.PeriodEnd is null || dated.Count == 0)
        {
            return new RevisionDecision(RevisionOutcome.Store, null);
        }

        var candidateTime = candidate.BroadcastTs!.Value.UtcDateTime;
        if (dated.Any(f => f.BroadcastTs!.Value.UtcDateTime == candidateTime))
        {
            return new RevisionDecision(RevisionOutcome.AmbiguousOrder, null);
        }

        var head = dated.OrderByDescending(f => f.BroadcastTs!.Value.UtcDateTime).ThenBy(f => f.Id).First();
        if (candidateTime < head.BroadcastTs!.Value.UtcDateTime)
        {
            return new RevisionDecision(RevisionOutcome.StoreOutOfOrder, null);
        }

        return CanSupersede(head, candidate) ? new RevisionDecision(RevisionOutcome.Store, head.Id) : new RevisionDecision(RevisionOutcome.BadSupersede, null);
    }
}
