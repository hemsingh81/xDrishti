using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>Builders for stored board-meeting rows (P15-04 tests).</summary>
internal static class CalendarSamples
{
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public static readonly TimeSpan Lag = TimeSpan.FromMinutes(5);

    public static readonly DateOnly Q1 = new(2026, 6, 30);

    public static DateTimeOffset At(int month, int day, int hour = 0, int minute = 0, int second = 0) => new(2026, month, day, hour, minute, second, Ist);

    public static CalendarRow Row(int n, DateOnly meeting, DateTimeOffset? intimated, string status = MeetingRow.Scheduled, string purpose = MeetingRow.Results, DateOnly? period = null, Guid? supersedes = null, Guid? instrument = null)
    {
        var who = instrument ?? FilingSamples.Instrument;
        var p = period ?? Q1;
        var sha = FilingSamples.Sha(n);
        var filing = intimated is { } ts
            ? new StoredFiling(FilingId.Create(who, FilingKind.BoardMeeting, p, sha), who, FilingKind.BoardMeeting, p, ts.ToUniversalTime(), BroadcastState.Ok, null, FilingSamples.Ingested, sha, 10, "m.csv", supersedes)
            : new StoredFiling(FilingId.Create(who, FilingKind.BoardMeeting, p, sha), who, FilingKind.BoardMeeting, p, null, BroadcastState.Unavailable, "missing", FilingSamples.Ingested, sha, 10, "m.csv", null);
        return new CalendarRow(filing, meeting, purpose, status);
    }

    public static TradingCalendar WithHoliday(int month, int day) => new([TradingDay.Holiday(new DateOnly(2026, month, day), CalendarSource.Manual, CalendarStatus.Confirmed, null, FilingSamples.Ingested)]);
}
