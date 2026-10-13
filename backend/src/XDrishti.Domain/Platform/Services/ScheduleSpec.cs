using System.Globalization;

namespace XDrishti.Domain.Platform.Services;

/// <summary>
/// A <see cref="Schedule"/> as text: what the API sends and receives and what the database stores (JSON). <c>type</c> is daily · weekly · after · window · interval · continuous; times are
/// <c>HH:mm</c> India time; <c>days</c> are mon … sun; <c>weekday</c> is one of them; <c>after</c> is a service id. Fields that do not belong to the type are left out (null).
/// </summary>
public sealed record ScheduleSpec(
    string Type,
    string? Time = null,
    IReadOnlyList<string>? Days = null,
    bool TradingDaysOnly = false,
    string? Weekday = null,
    string? After = null,
    string? From = null,
    string? To = null,
    int? EveryMinutes = null)
{
    public const string TimeFormat = "HH:mm";

    public static ScheduleSpec Of(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return new ScheduleSpec(
            schedule.Type.ToCode(),
            Clock(schedule.Time),
            schedule.Type is ScheduleType.Daily or ScheduleType.Window or ScheduleType.Interval ? schedule.Days.ToCodes() : null,
            schedule.TradingDaysOnly,
            schedule.Weekday is { } day ? WeekdaySets.CodeOf(day) : null,
            schedule.After,
            Clock(schedule.From),
            Clock(schedule.To),
            schedule.EveryMinutes);
    }

    /// <summary>
    /// Reads the text into a <see cref="Schedule"/>. Every unreadable or missing field of the type is added to <paramref name="issues"/> (errors on <paramref name="serviceId"/>, fields
    /// <c>schedule.type</c>, <c>schedule.time</c> …) and the result is then null. Whether the values make sense (an end after the start, a day chosen) is for <see cref="ScheduleRules"/>.
    /// </summary>
    public Schedule? Parse(string serviceId, ICollection<ScheduleIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var before = issues.Count;
        if (!ScheduleTypeCodes.TryParse(Type, out var type))
        {
            issues.Add(ScheduleIssue.Error(serviceId, "schedule.type", ScheduleIssueCodes.TypeInvalid, "Use daily, weekly, after, window, interval or continuous."));
            return null;
        }

        TimeOnly? time = null;
        TimeOnly? from = null;
        TimeOnly? to = null;
        var days = Weekdays.None;
        DayOfWeek? weekday = null;
        switch (type)
        {
            case ScheduleType.Daily:
                time = RequiredTime(serviceId, "schedule.time", Time, issues);
                days = ReadDays(serviceId, issues);
                break;
            case ScheduleType.Weekly:
                time = RequiredTime(serviceId, "schedule.time", Time, issues);
                if (WeekdaySets.TryParseDay(Weekday, out var day))
                {
                    weekday = day;
                }
                else
                {
                    issues.Add(ScheduleIssue.Error(serviceId, "schedule.weekday", ScheduleIssueCodes.DayInvalid, "Choose a weekday (mon to sun)."));
                }

                break;
            case ScheduleType.Window or ScheduleType.Interval:
                from = RequiredTime(serviceId, "schedule.from", From, issues);
                to = RequiredTime(serviceId, "schedule.to", To, issues);
                days = ReadDays(serviceId, issues);
                break;
            default:
                break;
        }

        if (issues.Count > before)
        {
            return null;
        }

        var tradingDaysOnly = TradingDaysOnly && type is ScheduleType.Daily or ScheduleType.Weekly or ScheduleType.Window or ScheduleType.Interval;
        return new Schedule(
            type,
            time,
            type is ScheduleType.Daily or ScheduleType.Window or ScheduleType.Interval ? (tradingDaysOnly ? Weekdays.MondayToFriday : days) : Weekdays.None,
            tradingDaysOnly,
            weekday,
            type == ScheduleType.After ? After?.Trim() : null,
            from,
            to,
            type == ScheduleType.Interval ? EveryMinutes : null);
    }

    public static string? Clock(TimeOnly? time) => time?.ToString(TimeFormat, CultureInfo.InvariantCulture);

    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static TimeOnly? RequiredTime(string serviceId, string field, string? text, ICollection<ScheduleIssue> issues)
    {
        if (TryParseTime(text, out var time))
        {
            return time;
        }

        issues.Add(ScheduleIssue.Error(serviceId, field, ScheduleIssueCodes.TimeInvalid, "Use the time as HH:mm (24-hour, India time)."));
        return null;
    }

    private Weekdays ReadDays(string serviceId, ICollection<ScheduleIssue> issues)
    {
        if (WeekdaySets.TryParse(Days, out var set))
        {
            return set;
        }

        issues.Add(ScheduleIssue.Error(serviceId, "schedule.days", ScheduleIssueCodes.DayInvalid, "Use the days as mon, tue, wed, thu, fri, sat, sun."));
        return Weekdays.None;
    }
}
