namespace XDrishti.Domain.Options.Quality;

/// <summary>How many sessions ended in an outcome: <c>pass</c>, <c>warn</c>, <c>fail</c> or <c>unavailable</c> (then with its reason).</summary>
public sealed record SessionOutcome(string Outcome, string? Reason, int Sessions);

/// <summary>The counts of the chain sessions of one underlying: recorded, usable, the first and last recorded date and the roll-up outcomes.</summary>
public sealed record ArchiveFigures(int Checked, int Recorded, int Usable, DateOnly? First, DateOnly? Last, IReadOnlyList<SessionOutcome> Outcomes);

/// <summary>
/// The pure count behind the archive summary (spec P14-16): from the newest stored checks of each chain session. A session is <b>recorded</b> unless its coverage check is <c>unavailable</c> with the reason <c>not_recorded</c>;
/// <b>usable</b> by <see cref="SessionUsability"/>; its outcome is the worst of its checks (<see cref="QualityStatuses.Worst"/>), with the reason of the first unavailable check when that is the outcome. Counts and dates only.
/// </summary>
public static class ArchiveSessionFigures
{
    public static ArchiveFigures Of(IReadOnlyDictionary<DateOnly, IReadOnlyList<QualityCheck>> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        int recorded = 0, usable = 0;
        DateOnly? first = null, last = null;
        var outcomes = new SortedDictionary<(int Rank, string Reason), int>();
        foreach (var (date, checks) in sessions)
        {
            var coverage = checks.FirstOrDefault(c => c.Code == QualityCodes.Coverage);
            var notRecorded = coverage is { Status: QualityStatus.Unavailable, Reason: QualityCodes.NotRecorded };
            if (coverage is not null && !notRecorded)
            {
                recorded++;
                first = first is null || date < first ? date : first;
                last = last is null || date > last ? date : last;
            }

            usable += SessionUsability.IsUsable(QualityCodes.Chain, [.. checks]) ? 1 : 0;
            var worst = QualityStatuses.Worst(checks.Select(c => c.Status));
            var reason = worst == QualityStatus.Unavailable ? checks.Where(c => c.Status == QualityStatus.Unavailable && c.Reason is not null).OrderBy(c => c.Code, StringComparer.Ordinal).Select(c => c.Reason).FirstOrDefault() : null;
            var key = (worst switch { QualityStatus.Pass => 0, QualityStatus.Warn => 1, QualityStatus.Fail => 2, _ => 3 }, reason ?? string.Empty);
            outcomes[key] = outcomes.GetValueOrDefault(key) + 1;
        }

        var list = outcomes.Select(o => new SessionOutcome(o.Key.Rank switch { 0 => "pass", 1 => "warn", 2 => "fail", _ => "unavailable" }, o.Key.Reason.Length == 0 ? null : o.Key.Reason, o.Value)).ToList();
        return new ArchiveFigures(sessions.Count, recorded, usable, first, last, list);
    }
}
