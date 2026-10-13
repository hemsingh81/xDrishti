using System.Text.Json;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Domain.Tests.Platform.Services;

/// <summary>Instants, calendars and test definitions shared by the schedule tests. October 2026: Mon 12th, Fri 9th, Sat 10th, Sun 11th.</summary>
internal static class ServiceTestData
{
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public static readonly DateTimeOffset Stamp = new(2026, 10, 1, 0, 0, 0, Ist);

    public static readonly IReadOnlyDictionary<string, JsonElement> NoParameters = new Dictionary<string, JsonElement>();

    public static DateTimeOffset At(int month, int day, int hour, int minute, int second = 0) => new(2026, month, day, hour, minute, second, Ist);

    public static DateOnly Date(int month, int day) => new(2026, month, day);

    public static TimeOnly T(int hour, int minute) => new(hour, minute);

    public static TradingDay Holiday(int month, int day) => TradingDay.Holiday(Date(month, day), CalendarSource.Manual, CalendarStatus.Confirmed, null, Stamp);

    public static TradingDay Special(int month, int day, int fromMinute, int toMinute) =>
        TradingDay.Special(Date(month, day), fromMinute, toMinute, CalendarSource.Manual, CalendarStatus.Confirmed, null, Stamp);

    public static ServiceSettings Settings(Schedule schedule, bool enabled = true, int retries = 0, int timeout = 30) =>
        new(enabled, schedule, retries, timeout, true, true, NoParameters);

    public static readonly ScheduleType[] AllTypes = Enum.GetValues<ScheduleType>();

    /// <summary>A definition for the tests: any schedule type allowed unless given.</summary>
    public static ServiceDefinition Definition(
        string id,
        Schedule schedule,
        ServiceConstraints? constraints = null,
        IReadOnlyList<ScheduleType>? allowed = null,
        IReadOnlyList<ParameterDefinition>? parameters = null) =>
        new(id, Title(id), $"The {id} service", "xd-test", allowed ?? AllTypes, Settings(schedule), parameters ?? [], constraints ?? new ServiceConstraints());

    public static RuleSubject Subject(string id, Schedule schedule, ServiceConstraints? constraints = null, bool enabled = true, IReadOnlyList<ScheduleType>? allowed = null) =>
        new(Definition(id, schedule, constraints, allowed), Settings(schedule, enabled));

    public static string Title(string id) => char.ToUpperInvariant(id[0]) + id[1..];
}
