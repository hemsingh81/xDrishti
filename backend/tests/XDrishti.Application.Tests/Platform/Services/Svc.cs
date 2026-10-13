using System.Text.Json;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>Test services: definitions like the real ones and like the ones that do not exist yet (nightly pipeline, backup).</summary>
internal static class Svc
{
    public static readonly IReadOnlyDictionary<string, JsonElement> NoParameters = new Dictionary<string, JsonElement>();

    public static TimeOnly T(int hour, int minute) => new(hour, minute);

    public static ServiceSettings Settings(Schedule schedule, bool enabled = true, int retries = 0, int timeout = 30, bool catchUp = true, bool notify = true) =>
        new(enabled, schedule, retries, timeout, catchUp, notify, NoParameters);

    public static TestService Make(
        string id,
        Schedule schedule,
        bool enabled = true,
        int retries = 0,
        int timeout = 30,
        bool catchUp = true,
        bool notify = true,
        ServiceConstraints? constraints = null,
        IReadOnlyList<ParameterDefinition>? parameters = null,
        IReadOnlyList<ScheduleType>? allowed = null) =>
        new(new ServiceDefinition(
            id,
            char.ToUpperInvariant(id[0]) + id[1..],
            $"The {id} service",
            "xd-test",
            allowed ?? Enum.GetValues<ScheduleType>(),
            Settings(schedule, enabled, retries, timeout, catchUp, notify),
            parameters ?? [],
            constraints ?? new ServiceConstraints()));

    /// <summary>Like the end-of-day run: every trading day at 15:50, never before the session has ended, warned about before 15:35.</summary>
    public static TestService Eod(Schedule? schedule = null, bool enabled = true, int retries = 0, bool catchUp = true, bool notify = true, CatchUpReach reach = CatchUpReach.SameDay) =>
        Make("eod", schedule ?? Schedule.Daily(T(15, 50)), enabled, retries, 30, catchUp, notify, new ServiceConstraints
        {
            StartNotBefore = T(15, 35),
            StartAdvice = "The session ends at 15:30.",
            NotBeforeSessionEnd = true,
            CatchUpReach = reach,
        });

    /// <summary>A nightly pipeline that must follow the end-of-day run and the account sync (which is not a registered service).</summary>
    public static TestService Nightly(Schedule? schedule = null) =>
        Make("nightly", schedule ?? Schedule.Daily(T(18, 30)), constraints: new ServiceConstraints { MustFollow = ["eod", "account-sync"] });

    public static TestService Backup(Schedule? schedule = null) =>
        Make("backup", schedule ?? Schedule.Daily(T(23, 30), false, Weekdays.EveryDay), constraints: new ServiceConstraints { MustNotOverlap = TimeRange.MarketHours });

    public static ServiceScheduleInput Input(Schedule schedule, long revision = 0, bool enabled = true, int retries = 0, int timeout = 30, bool catchUp = true, bool notify = true, IReadOnlyDictionary<string, JsonElement>? parameters = null) =>
        new(enabled, ScheduleSpec.Of(schedule), retries, timeout, catchUp, notify, parameters, revision);
}
