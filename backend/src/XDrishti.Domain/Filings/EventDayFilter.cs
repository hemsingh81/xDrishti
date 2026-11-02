using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The strategy filter <c>not_event_day</c> (P15-06, pure): "no position is open across a results event". An entry decided at <c>t</c> is <see cref="WindowOutcome.Inside"/> (blocked) iff the next results event session is 0 to H sessions away, with H the side's
/// <c>max_hold_sessions</c> (0 for a side that closes within the day), counted by the P15-04 rule from the rows visible at <c>t</c>. No visible entry is <see cref="WindowOutcome.Unavailable"/>: the simulator takes no entry and counts it.
/// </summary>
public static class EventDayFilter
{
    /// <summary>The outcome for an entry decided at <paramref name="decisionInstant"/>.</summary>
    public static WindowOutcome Evaluate(EventCalendarSnapshot snapshot, Guid instrumentId, DateTimeOffset decisionInstant, TradingCalendar calendar, int holdSessions)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(calendar);
        if (holdSessions < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(holdSessions), holdSessions, "A hold is 0 or more sessions.");
        }

        return ResultsWindow.Classify(snapshot.Answer(instrumentId, decisionInstant, calendar), holdSessions);
    }
}
