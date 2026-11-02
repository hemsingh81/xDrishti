using XDrishti.Domain.Filings;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>Builders for the results-calendar snapshot of the event-filter tests (P15-06): one results meeting per instrument, intimated at a given India time.</summary>
internal static class EventRows
{
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    /// <summary>The period every meeting reports.</summary>
    public static readonly DateOnly Period = new(2025, 12, 31);

    /// <summary>A meeting of <paramref name="instrument"/> on <paramref name="meeting"/>, intimated on 2026-02-20 at 11:00 IST unless said otherwise; <paramref name="n"/> makes the row's content hash.</summary>
    public static CalendarRow Meeting(Guid instrument, DateOnly meeting, int n = 1, DateTimeOffset? intimated = null, string status = MeetingRow.Scheduled, DateOnly? period = null, Guid? supersedes = null)
    {
        var sha = n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
        var p = period ?? Period;
        var ts = (intimated ?? new DateTimeOffset(2026, 2, 20, 11, 0, 0, Ist)).ToUniversalTime();
        var filing = new StoredFiling(FilingId.Create(instrument, FilingKind.BoardMeeting, p, sha), instrument, FilingKind.BoardMeeting, p, ts, BroadcastState.Ok, null, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), sha, 10, "m.csv", supersedes);
        return new CalendarRow(filing, meeting, MeetingRow.Results, status);
    }

    public static EventCalendarSnapshot Snapshot(Guid instrument, DateOnly meeting, int lagMinutes = 5, DateTimeOffset? intimated = null) =>
        EventCalendarSnapshot.Create([Meeting(instrument, meeting, intimated: intimated)], lagMinutes);
}
