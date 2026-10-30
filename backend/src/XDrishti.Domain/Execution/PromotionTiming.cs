using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Execution;

/// <summary>The window in which a promotion request can be confirmed.</summary>
public sealed record PromotionWindow(DateTimeOffset EligibleFrom, DateTimeOffset ExpiresAt);

/// <summary>
/// The cooling-off of a promotion (spec P12-07 D1): confirmable from the close of the second trading session after the request until the close of the fifth, and in force from 09:00 India time of the next trading day. Pure over the calendar.
/// </summary>
public static class PromotionTiming
{
    public const int CoolingOffSessions = 2;

    public const int ExpirySessions = 5;

    public const int OpenMinute = 9 * 60;

    public static PromotionWindow Window(DateTimeOffset requestedAt, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var day = IndiaTime.Today(requestedAt);
        DateTimeOffset? eligible = null;
        DateTimeOffset close = default;
        for (var session = 1; session <= ExpirySessions; session++)
        {
            day = NextTradingDay(day, calendar);
            close = CloseOf(day, calendar);
            if (session == CoolingOffSessions)
            {
                eligible = close;
            }
        }

        return new PromotionWindow(eligible!.Value, close);
    }

    /// <summary>09:00 India time of the first trading day after the date of <paramref name="now"/>.</summary>
    public static DateTimeOffset EffectiveFrom(DateTimeOffset now, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var day = NextTradingDay(IndiaTime.Today(now), calendar);
        return AtMinute(day, OpenMinute);
    }

    private static DateOnly NextTradingDay(DateOnly after, TradingCalendar calendar)
    {
        var day = after;
        for (var guard = 0; guard < 400; guard++)
        {
            day = day.AddDays(1);
            if (calendar.IsTradingDay(day))
            {
                return day;
            }
        }

        throw new InvalidOperationException("The calendar has no trading day in the next 400 days.");
    }

    private static DateTimeOffset CloseOf(DateOnly day, TradingCalendar calendar) => AtMinute(day, calendar.SessionOf(day)!.Value.ToMinute);

    private static DateTimeOffset AtMinute(DateOnly day, int minute) => new(day.ToDateTime(TimeOnly.MinValue).AddMinutes(minute), IndiaTime.Offset);
}
