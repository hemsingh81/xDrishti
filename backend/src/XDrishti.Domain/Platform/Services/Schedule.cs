using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Platform.Services;

/// <summary>
/// When a service runs (ADR 0018 §2): the checked form of <see cref="ScheduleSpec"/>. Which fields matter depends on <see cref="Type"/>: daily — <see cref="Time"/> and the days; weekly —
/// <see cref="Weekday"/> and <see cref="Time"/>; after — <see cref="After"/>; window — <see cref="From"/> and <see cref="To"/> and the days; interval — <see cref="EveryMinutes"/> within
/// <see cref="From"/> to <see cref="To"/> and the days; continuous — none. All times are India time. With <see cref="TradingDaysOnly"/> the trading calendar decides which dates count
/// (a regular weekday, except holidays; a special session on a weekend counts) and <see cref="Days"/> is ignored; otherwise <see cref="Days"/> does.
/// </summary>
public sealed record Schedule(
    ScheduleType Type,
    TimeOnly? Time = null,
    Weekdays Days = Weekdays.None,
    bool TradingDaysOnly = false,
    DayOfWeek? Weekday = null,
    string? After = null,
    TimeOnly? From = null,
    TimeOnly? To = null,
    int? EveryMinutes = null)
{
    public static Schedule Daily(TimeOnly time, bool tradingDaysOnly = true, Weekdays days = Weekdays.MondayToFriday) =>
        new(ScheduleType.Daily, Time: time, Days: tradingDaysOnly ? Weekdays.MondayToFriday : days, TradingDaysOnly: tradingDaysOnly);

    public static Schedule Weekly(DayOfWeek weekday, TimeOnly time, bool tradingDaysOnly = false) =>
        new(ScheduleType.Weekly, Time: time, Weekday: weekday, TradingDaysOnly: tradingDaysOnly);

    public static Schedule AfterService(string serviceId) => new(ScheduleType.After, After: serviceId);

    public static Schedule Window(TimeOnly from, TimeOnly to, bool tradingDaysOnly = true, Weekdays days = Weekdays.MondayToFriday) =>
        new(ScheduleType.Window, Days: tradingDaysOnly ? Weekdays.MondayToFriday : days, TradingDaysOnly: tradingDaysOnly, From: from, To: to);

    public static Schedule Interval(int everyMinutes, TimeOnly from, TimeOnly to, bool tradingDaysOnly = true, Weekdays days = Weekdays.MondayToFriday) =>
        new(ScheduleType.Interval, Days: tradingDaysOnly ? Weekdays.MondayToFriday : days, TradingDaysOnly: tradingDaysOnly, From: from, To: to, EveryMinutes: everyMinutes);

    public static Schedule Continuous() => new(ScheduleType.Continuous);

    /// <summary>
    /// What a stored schedule that cannot be read is read as (damaged JSON, a time that does not parse): never saved, never run. The service is shown on its defaults with an error until a new schedule is saved
    /// (<see cref="ServiceSchedule.ScheduleReadable"/>).
    /// </summary>
    public static readonly Schedule Unreadable = new(ScheduleType.After, After: "(unreadable)");

    /// <summary>Whether the schedule fires on the date at all (a service that follows another or is always up has no days of its own).</summary>
    public bool FiresOn(DateOnly date, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return Type switch
        {
            ScheduleType.Daily or ScheduleType.Window or ScheduleType.Interval => TradingDaysOnly ? calendar.IsTradingDay(date) : Days.Includes(date.DayOfWeek),
            ScheduleType.Weekly => Weekday == date.DayOfWeek && (!TradingDaysOnly || calendar.IsTradingDay(date)),
            ScheduleType.Continuous => true,
            _ => false,
        };
    }

    /// <summary>The days of the week the schedule can fire on (a trading-days schedule: Monday to Friday, special sessions aside).</summary>
    public Weekdays DaySet => Type switch
    {
        ScheduleType.Weekly => Weekday is { } day ? WeekdaySets.Of(day) : Weekdays.None,
        ScheduleType.Continuous => Weekdays.EveryDay,
        ScheduleType.After => Weekdays.None,
        _ => TradingDaysOnly ? Weekdays.MondayToFriday : Days,
    };

    /// <summary>The time of day the service is started at (the start of a window or interval); null for a schedule that follows another or is always up.</summary>
    public TimeOnly? StartTime => Type switch
    {
        ScheduleType.Daily or ScheduleType.Weekly => Time,
        ScheduleType.Window or ScheduleType.Interval => From,
        _ => null,
    };
}
