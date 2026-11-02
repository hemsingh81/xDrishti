using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The results event instants of each instrument, for the <c>news_results</c> loss tag (P15-06, ADR 0182, pure). An instrument's events are the broadcast times of its stored <c>results</c> filings, and, for each calendar meeting that is current (the latest visible row of its
/// period as of <c>asOf</c>), not cancelled, and has <b>no</b> results filing for the same period, the 09:15 India time of its event session (<c>FromCalendar</c>). An instrument that has neither a results filing nor a calendar row is not in the answer: nothing is known about it,
/// which is not the same as "no events". The answer describes the past from data known after it; it is never a decision-time input.
/// </summary>
public static class ResultsEvents
{
    private static readonly TimeSpan SessionOpen = new(9, 15, 0);

    public static IReadOnlyDictionary<Guid, IReadOnlyList<EventInstant>> Of(IEnumerable<CalendarRow> calendarRows, IEnumerable<ResultsFilingTime> filings, TradingCalendar calendar, TimeSpan lag, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(calendarRows);
        ArgumentNullException.ThrowIfNull(filings);
        ArgumentNullException.ThrowIfNull(calendar);
        var filed = filings.ToLookup(f => f.InstrumentId);
        var meetings = calendarRows.Where(r => r.Purpose == MeetingRow.Results).ToLookup(r => r.Filing.InstrumentId);
        var result = new Dictionary<Guid, IReadOnlyList<EventInstant>>();
        foreach (var id in filed.Select(g => g.Key).Concat(meetings.Select(g => g.Key)).Distinct().Order())
        {
            var instants = filed[id].Select(f => new EventInstant(f.BroadcastTs, false)).ToList();
            var covered = filed[id].Where(f => f.PeriodEnd is not null).Select(f => f.PeriodEnd!.Value).ToHashSet();
            var byId = meetings[id].ToDictionary(r => r.Filing.Id);
            foreach (var filing in PointInTimeRule.AsOf(byId.Values.Select(r => r.Filing), asOf, lag).Filings)
            {
                var row = byId[filing.Id];
                if (row.IsCancelled || (filing.PeriodEnd is { } period && covered.Contains(period)) || ResultsDays.EventSession(calendar, row.MeetingDate) is not { } session)
                {
                    continue;
                }

                var open = new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue) + SessionOpen, IndiaTime.Offset);
                instants.Add(new EventInstant(open.ToUniversalTime(), true));
            }

            result[id] = [.. instants.OrderBy(i => i.At.UtcTicks).ThenBy(i => i.FromCalendar)];
        }

        return result;
    }
}
