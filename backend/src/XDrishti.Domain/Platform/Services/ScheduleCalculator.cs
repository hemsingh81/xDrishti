using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Platform.Services;

/// <summary>
/// The pure answers about a <see cref="Schedule"/> (ADR 0018 §3): when it fires on a date, the next occurrence after an instant, the latest one up to an instant and whether one was missed.
/// No I/O: the caller passes the calendar and the instants. India time has no daylight saving, so a fixed offset is exact. With <c>notBeforeSessionEnd</c> a daily or weekly run on a
/// trading day never starts before that day's session ends (ADR 0017 §2): a special session that ends later moves the run to its end.
/// A schedule that follows another service (<see cref="ScheduleType.After"/>) or is always up has no occurrences of its own.
/// </summary>
public static class ScheduleCalculator
{
    /// <summary>How far ahead (or back) the search for a date that fires goes: a calendar full of holidays must not loop for ever.</summary>
    private const int SearchDays = 400;

    /// <summary>A weekly run missed within this long is still made up for.</summary>
    private static readonly TimeSpan WeeklyReach = TimeSpan.FromHours(24);

    /// <summary>The instants the schedule fires on the India date, in order. A window has one (its start), an interval every step from its start up to and including its end.</summary>
    public static IReadOnlyList<DateTimeOffset> OccurrencesOn(Schedule schedule, DateOnly date, TradingCalendar calendar, bool notBeforeSessionEnd = false)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(calendar);
        if (!schedule.FiresOn(date, calendar))
        {
            return [];
        }

        switch (schedule.Type)
        {
            case ScheduleType.Daily or ScheduleType.Weekly when schedule.Time is { } time:
                return [At(date, RunTime(calendar, date, time, notBeforeSessionEnd))];
            case ScheduleType.Window when schedule.From is { } from:
                return [At(date, from)];
            case ScheduleType.Interval when schedule is { From: { } start, To: { } end, EveryMinutes: { } step and > 0 }:
                var ticks = new List<DateTimeOffset>();
                for (var at = At(date, start); at <= At(date, end); at = at.AddMinutes(step))
                {
                    ticks.Add(at);
                }

                return ticks;
            default:
                return [];
        }
    }

    /// <summary>The part of the date the service is up (a window, an interval's window, the whole day for a continuous one); empty for the other types.</summary>
    public static IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> BandsOn(Schedule schedule, DateOnly date, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(calendar);
        if (!schedule.FiresOn(date, calendar))
        {
            return [];
        }

        return schedule switch
        {
            { Type: ScheduleType.Window or ScheduleType.Interval, From: { } from, To: { } to } => [(At(date, from), At(date, to))],
            { Type: ScheduleType.Continuous } => [(At(date, TimeOnly.MinValue), At(date.AddDays(1), TimeOnly.MinValue))],
            _ => [],
        };
    }

    /// <summary>The first occurrence strictly after <paramref name="after"/>; null when the schedule has none (it follows another service, is always up, or the calendar has no matching day in the next 400 days).</summary>
    public static DateTimeOffset? NextAfter(Schedule schedule, DateTimeOffset after, TradingCalendar calendar, bool notBeforeSessionEnd = false)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.Type is ScheduleType.After or ScheduleType.Continuous)
        {
            return null;
        }

        var today = IndiaTime.Today(after);
        for (var ahead = 0; ahead <= SearchDays; ahead++)
        {
            foreach (var occurrence in OccurrencesOn(schedule, today.AddDays(ahead), calendar, notBeforeSessionEnd))
            {
                if (occurrence > after)
                {
                    return occurrence;
                }
            }
        }

        return null;
    }

    /// <summary>The next <paramref name="count"/> occurrences after <paramref name="after"/>, for the preview of a schedule being edited.</summary>
    public static IReadOnlyList<DateTimeOffset> NextOccurrences(Schedule schedule, DateTimeOffset after, int count, TradingCalendar calendar, bool notBeforeSessionEnd = false)
    {
        var found = new List<DateTimeOffset>();
        var cursor = after;
        while (found.Count < count && NextAfter(schedule, cursor, calendar, notBeforeSessionEnd) is { } next)
        {
            found.Add(next);
            cursor = next;
        }

        return found;
    }

    /// <summary>The latest occurrence at or before <paramref name="at"/>; null when there is none in the last 400 days.</summary>
    public static DateTimeOffset? LatestAtOrBefore(Schedule schedule, DateTimeOffset at, TradingCalendar calendar, bool notBeforeSessionEnd = false)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.Type is ScheduleType.After or ScheduleType.Continuous)
        {
            return null;
        }

        var today = IndiaTime.Today(at);
        for (var back = 0; back <= SearchDays; back++)
        {
            var occurrences = OccurrencesOn(schedule, today.AddDays(-back), calendar, notBeforeSessionEnd);
            for (var i = occurrences.Count - 1; i >= 0; i--)
            {
                if (occurrences[i] <= at)
                {
                    return occurrences[i];
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The occurrence that was missed, or null: a daily or weekly schedule whose latest occurrence is within reach (<see cref="CatchUpReach"/>) of <paramref name="now"/> and with no run started
    /// at or after it (<paramref name="lastRunStartedAt"/>, a run of any kind and outcome). Windows, intervals and the other types are not made up for.
    /// </summary>
    public static DateTimeOffset? MissedOccurrence(Schedule schedule, DateTimeOffset now, DateTimeOffset? lastRunStartedAt, TradingCalendar calendar, bool notBeforeSessionEnd = false, CatchUpReach reach = CatchUpReach.SameDay)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.Type is not (ScheduleType.Daily or ScheduleType.Weekly) || LatestAtOrBefore(schedule, now, calendar, notBeforeSessionEnd) is not { } occurrence)
        {
            return null;
        }

        var within = reach == CatchUpReach.Latest
            || (schedule.Type == ScheduleType.Weekly ? now - occurrence <= WeeklyReach : IndiaTime.Today(occurrence) == IndiaTime.Today(now));
        return within && (lastRunStartedAt is null || lastRunStartedAt < occurrence) ? occurrence : null;
    }

    /// <summary>The time a daily or weekly run starts on a date: the configured time, but not before the session has closed when asked to.</summary>
    private static TimeOnly RunTime(TradingCalendar calendar, DateOnly date, TimeOnly time, bool notBeforeSessionEnd)
    {
        if (!notBeforeSessionEnd || calendar.SessionOf(date) is not { } session)
        {
            return time;
        }

        var close = new TimeOnly(session.ToMinute / 60, session.ToMinute % 60);
        return time > close ? time : close;
    }

    private static DateTimeOffset At(DateOnly date, TimeOnly time) => new(date.ToDateTime(time, DateTimeKind.Unspecified), IndiaTime.Offset);
}
