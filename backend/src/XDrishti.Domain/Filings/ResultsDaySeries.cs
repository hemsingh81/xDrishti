using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The per-date frame series of one instrument (P15-05, pure): for each trading date from <c>from</c> to <c>to</c>, <c>days_to_results</c> and <c>days_since_results</c> as known at the <b>start</b> of the date (00:00 India time): rows and filings with
/// <c>intimation or broadcast + lag</c> at or before that instant. A meeting intimated at 11:00 on a date therefore counts from the next date, which is never earlier than the instant rule of <c>not_event_day</c>, so the series can only see less.
/// <para>
/// <c>days_to_results</c> is <see cref="ResultsDays.Upcoming"/> at that instant (NaN when there is no upcoming visible row). <c>days_since_results</c> counts the sessions from the reaction session <see cref="ReactionSession"/> of the newest visible results filing to the date
/// (0 on the reaction session); NaN when no filing is visible or its reaction session is after the date. Computed once per instrument and run; the walk reads a dictionary.
/// </para>
/// </summary>
public sealed class ResultsDaySeries
{
    private ResultsDaySeries(Dictionary<DateOnly, ResultsDayPoint> points)
    {
        Points = points;
    }

    /// <summary>The version of these rules, part of the run key of a run that reads them.</summary>
    public const string Version = "rds-1";

    public IReadOnlyDictionary<DateOnly, ResultsDayPoint> Points { get; }

    public static ResultsDaySeries Build(IReadOnlyList<CalendarRow> rows, IReadOnlyList<ResultsFilingTime> filings, TimeSpan lag, TradingCalendar calendar, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(filings);
        ArgumentNullException.ThrowIfNull(calendar);
        var points = new Dictionary<DateOnly, ResultsDayPoint>();
        var byVisible = rows.Where(r => r.Filing.BroadcastTs is not null).Select(r => r.Filing.BroadcastTs!.Value + lag).Order().ToList();
        var visibleFilings = filings.OrderBy(f => f.BroadcastTs.UtcTicks).ThenBy(f => f.PeriodEnd).ToList();
        var seenRows = -1;
        DateOnly? session = null;
        var seenFilings = 0;
        ResultsFilingTime? newest = null;
        DateOnly? reaction = null;
        var since = double.NaN;
        var sinceValid = false;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
            var trading = calendar.IsTradingDay(date);

            // days to results: the visible rows change only when an intimation becomes visible; the earliest upcoming session changes then, or when the date passes it
            var visibleRows = CountUpTo(byVisible, start);
            if (visibleRows != seenRows || session is { } old && old < date)
            {
                seenRows = visibleRows;
                var upcoming = ResultsDays.Upcoming(rows, start, lag, calendar);
                session = upcoming.Count > 0 ? upcoming[0].EventSession : null;
            }

            // days since results: the newest visible filing by broadcast time (ties: the later period)
            var changed = false;
            while (seenFilings < visibleFilings.Count && visibleFilings[seenFilings].BroadcastTs + lag <= start)
            {
                if (newest is null || visibleFilings[seenFilings].BroadcastTs >= newest.BroadcastTs)
                {
                    newest = visibleFilings[seenFilings];
                    changed = true;
                }

                seenFilings++;
            }

            if (changed)
            {
                reaction = ReactionSession.Of(newest!.BroadcastTs, lag, calendar);
                sinceValid = false;
            }

            if (reaction is null || reaction.Value > date)
            {
                since = double.NaN;
                sinceValid = false;
            }
            else if (!sinceValid)
            {
                since = calendar.TradingDaysBetween(reaction.Value, date);
                sinceValid = true;
            }
            else if (trading)
            {
                since++;
            }

            if (trading)
            {
                points[date] = new ResultsDayPoint(session is { } s && s >= date ? calendar.TradingDaysBetween(date, s) : double.NaN, since);
            }
        }

        return new ResultsDaySeries(points);
    }

    private static int CountUpTo(List<DateTimeOffset> sorted, DateTimeOffset instant)
    {
        var low = 0;
        var high = sorted.Count;
        while (low < high)
        {
            var mid = (low + high) >>> 1;
            if (sorted[mid] <= instant)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
