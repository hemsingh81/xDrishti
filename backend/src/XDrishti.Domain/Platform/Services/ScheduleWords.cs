using System.Globalization;

namespace XDrishti.Domain.Platform.Services;

/// <summary>A schedule in words for the screen: "Every trading day at 15:50 IST".</summary>
public static class ScheduleWords
{
    /// <param name="schedule">The schedule.</param>
    /// <param name="nameOf">The name of a service by id (for a schedule that follows another); the id itself when unknown.</param>
    /// <param name="notBeforeSessionEnd">Whether the service never starts before the session has ended (said in words).</param>
    public static string Describe(Schedule schedule, Func<string, string> nameOf, bool notBeforeSessionEnd = false)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(nameOf);
        var days = schedule.TradingDaysOnly ? "on trading days" : "on " + schedule.Days.Describe();
        var text = schedule.Type switch
        {
            ScheduleType.Daily => $"{Every(schedule)} at {Hm(schedule.Time)} IST",
            ScheduleType.Weekly => $"Every {(schedule.Weekday is { } day ? WeekdaySets.NameOf(day) : "week")} at {Hm(schedule.Time)} IST{(schedule.TradingDaysOnly ? " (if it is a trading day)" : string.Empty)}",
            ScheduleType.After => $"Each time {nameOf(schedule.After ?? string.Empty)} finishes successfully",
            ScheduleType.Window => $"Up from {Hm(schedule.From)} to {Hm(schedule.To)} IST {days}",
            ScheduleType.Interval => $"Every {schedule.EveryMinutes?.ToString(CultureInfo.InvariantCulture)} min from {Hm(schedule.From)} to {Hm(schedule.To)} IST {days}",
            _ => "Always on",
        };
        return notBeforeSessionEnd && schedule.Type is ScheduleType.Daily or ScheduleType.Weekly ? text + ", never before the session has ended" : text;
    }

    private static string Every(Schedule schedule) =>
        schedule.TradingDaysOnly ? "Every trading day" : schedule.Days == Weekdays.EveryDay ? "Every day" : schedule.Days.Describe() is { } days ? char.ToUpperInvariant(days[0]) + days[1..] : "Never";

    private static string Hm(TimeOnly? time) => time?.ToString(ScheduleSpec.TimeFormat, CultureInfo.InvariantCulture) ?? "?";
}
