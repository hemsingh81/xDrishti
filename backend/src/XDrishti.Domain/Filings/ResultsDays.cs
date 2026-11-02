using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The days-to-results rule (P15-04, pure). For one instrument and an instant t: take the <c>results</c> rows visible at t by the point-in-time rule (intimation + lag &lt;= t), the current one per period end (a reschedule replaces the earlier row only from its own visible
/// time), drop the cancelled ones; the <b>event session</b> of a row is the first trading day on or after its meeting date; the candidates are those whose event session is on or after the India date of t; the earliest wins and <c>days = TradingDaysBetween(date(t), event session)</c>.
/// A row that was intimated after t, or has no intimation time, never contributes. No entry is <c>unavailable</c>, never a number.
/// </summary>
public static class ResultsDays
{
    private const int EventSessionLookaheadDays = 400;

    /// <summary>The first trading day on or after the meeting date; null when the calendar knows none within 400 days.</summary>
    public static DateOnly? EventSession(TradingCalendar calendar, DateOnly meetingDate)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        for (var i = 0; i <= EventSessionLookaheadDays; i++)
        {
            var date = meetingDate.AddDays(i);
            if (calendar.IsTradingDay(date))
            {
                return date;
            }
        }

        return null;
    }

    public static ResultsAnswer Compute(IEnumerable<CalendarRow> rows, DateTimeOffset t, TimeSpan lag, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var all = rows.ToList();
        var upcoming = Upcoming(all, t, lag, calendar);
        if (upcoming.Count > 0)
        {
            return ResultsAnswer.Of(upcoming[0]);
        }

        var current = CurrentRows(all, t, lag);
        if (current.Count == 0)
        {
            return ResultsAnswer.Missing(ResultsAnswer.NoEntry);
        }

        return ResultsAnswer.Missing(current.All(r => r.IsCancelled) ? ResultsAnswer.OnlyCancelled : ResultsAnswer.NoUpcomingEntry);
    }

    /// <summary>Every visible, current, not cancelled results meeting whose event session is not past, earliest first (ties by period end, then id).</summary>
    public static IReadOnlyList<UpcomingMeeting> Upcoming(IEnumerable<CalendarRow> rows, DateTimeOffset t, TimeSpan lag, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(calendar);
        var all = rows.Where(r => r.Purpose == MeetingRow.Results).ToList();
        var today = IndiaTime.Today(t);
        var visible = all.Where(r => PointInTimeRule.IsVisible(r.Filing, t, lag)).ToList();
        var result = new List<(UpcomingMeeting Meeting, Guid Id)>();
        foreach (var row in CurrentRows(all, t, lag).Where(r => !r.IsCancelled))
        {
            if (EventSession(calendar, row.MeetingDate) is not { } session || session < today)
            {
                continue;
            }

            result.Add((new UpcomingMeeting(row.Filing.InstrumentId, row.Filing.PeriodEnd!.Value, row.MeetingDate, session, calendar.TradingDaysBetween(today, session), row.Filing.BroadcastTs!.Value, Revision(row, visible)), row.Filing.Id));
        }

        return [.. result.OrderBy(r => r.Meeting.EventSession).ThenBy(r => r.Meeting.PeriodEnd).ThenBy(r => r.Id).Select(r => r.Meeting)];
    }

    // The current visible row per period end: the point-in-time rule decides which (the latest not superseded by a visible revision).
    private static List<CalendarRow> CurrentRows(IReadOnlyCollection<CalendarRow> results, DateTimeOffset t, TimeSpan lag)
    {
        var byId = results.Where(r => r.Purpose == MeetingRow.Results).ToDictionary(r => r.Filing.Id);
        var answer = PointInTimeRule.AsOf(byId.Values.Select(r => r.Filing), t, lag);
        return [.. answer.Filings.Select(f => byId[f.Id])];
    }

    // 1 plus the visible rows of the same instrument and period with an earlier time: the same whatever order the rows were loaded in.
    private static int Revision(CalendarRow row, List<CalendarRow> visible) =>
        1 + visible.Count(r => r.Filing.InstrumentId == row.Filing.InstrumentId && r.Filing.PeriodEnd == row.Filing.PeriodEnd && r.Filing.BroadcastTs < row.Filing.BroadcastTs);
}
