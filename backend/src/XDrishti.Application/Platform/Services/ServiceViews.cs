using System.Text.Json;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>How a service is run: retries (0–5 after a failed attempt), the timeout of an attempt in minutes (1–240), whether a missed run is made up for when the application starts, and whether a failure raises a notice in the bell.</summary>
public sealed record ExecutionView(int Retries, int TimeoutMinutes, bool CatchUp, bool NotifyOnFailure);

/// <summary>A parameter of a service with its schema: <c>kind</c> integer · number · boolean, the bounds of a number, the current value and the default.</summary>
public sealed record ParameterView(string Name, string Label, string Kind, double Min, double Max, JsonElement Value, JsonElement Default, string? Description, string? Unit);

/// <summary>One run (or attempt) of a service. <c>trigger</c>: scheduled · catch_up · after · manual. <c>status</c>: running · succeeded · failed · skipped. <c>dataJobId</c> links the data job a run started.</summary>
public sealed record ServiceRunView(Guid Id, string ServiceId, string Trigger, int Attempt, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status, string? Message, Guid? DataJobId)
{
    public static ServiceRunView From(ServiceRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new ServiceRunView(run.Id, run.ServiceId, run.Trigger.ToCode(), run.Attempt, run.StartedAt, run.FinishedAt, run.Status.ToCode(), run.Message, run.DataJobId);
    }
}

/// <summary>A finding of the schedule checks. <c>severity</c>: error (blocks saving) · warning. <c>field</c> is a dotted path like the request (schedule.time, retries, parameters.x); <c>relatedServiceId</c> names the other service of a finding that involves two.</summary>
public sealed record ServiceIssueView(string ServiceId, string Field, string Severity, string Code, string Message, string? RelatedServiceId)
{
    public static ServiceIssueView From(ScheduleIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new ServiceIssueView(issue.ServiceId, issue.Field, issue.IsError ? "error" : "warning", issue.Code, issue.Message, issue.RelatedServiceId);
    }
}

/// <summary>
/// A service on the Services screen (ADR 0018). <c>status</c>: healthy · running · failed (its latest run failed) · paused. <c>schedule</c> is the stored form (<c>scheduleWords</c> says it in words);
/// <c>allowedScheduleTypes</c> are the types the owner may choose; <c>isDefault</c> says the choices are the definition's defaults; <c>lastRun</c> is the latest run of any kind, <c>nextRun</c> the next
/// scheduled start (null when paused, when it follows another service, is always up or has no day ahead). <c>revision</c> is sent back with a save (0 until the first save or seed). A <c>readOnly</c>
/// entry is set elsewhere (<c>manageUrl</c>) and refuses writes.
/// </summary>
public sealed record ServiceView(
    string Id,
    string Name,
    string Description,
    string RunsAs,
    bool ReadOnly,
    string? ManageUrl,
    bool Enabled,
    string Status,
    ScheduleSpec Schedule,
    string ScheduleWords,
    IReadOnlyList<string> AllowedScheduleTypes,
    ExecutionView Execution,
    IReadOnlyList<ParameterView> Parameters,
    ServiceRunView? LastRun,
    DateTimeOffset? NextRun,
    IReadOnlyList<ServiceIssueView> Issues,
    bool IsDefault,
    long Revision);

/// <summary>Every service: the registered ones in screen order, then the read-only entries.</summary>
public sealed record ServiceList(IReadOnlyList<ServiceView> Items);

/// <summary>A page of runs, newest first. At most the latest <see cref="ServiceRunLimits.MaxShown"/> runs of a service are shown; <c>total</c> counts those.</summary>
public sealed record ServiceRunPage(IReadOnlyList<ServiceRunView> Items, int Page, int PageSize, int Total);

/// <summary>The dry run of a schedule: the findings (errors and warnings), the next three starts after now (empty when the schedule has errors or follows another service) and the schedule in words.</summary>
public sealed record SchedulePreview(IReadOnlyList<ServiceIssueView> Issues, IReadOnlyList<DateTimeOffset> NextRuns, string ScheduleWords);

/// <summary>One start on the timeline: the India time as <c>HH:mm</c> and the instant.</summary>
public sealed record TimelineMark(string Time, DateTimeOffset At);

/// <summary>A part of the day a service is up (<c>from</c>, <c>to</c> as <c>HH:mm</c>; a continuous service spans 00:00 to 24:00).</summary>
public sealed record TimelineBand(string From, string To);

/// <summary>
/// What one enabled service does on the day. <c>kind</c>: mark (starts at the marks) · band (up over the band; an interval also gives <c>everyMinutes</c>). <c>followsServiceId</c> names the service a
/// run-after service waits for (its mark is the one of the service it follows, at the end of the chain).
/// </summary>
public sealed record TimelineEntry(string ServiceId, string Name, string Kind, IReadOnlyList<TimelineMark> Marks, TimelineBand? Band, int? EveryMinutes, string? FollowsServiceId);

/// <summary>The trading-day timeline: market hours (null on a day without a session), the review cutoff, whether the date is a trading day and every enabled service's starts and bands.</summary>
public sealed record ServicesTimeline(string Date, bool TradingDay, string? MarketOpen, string? MarketClose, string ReviewCutoff, IReadOnlyList<TimelineEntry> Entries);

/// <summary>A failure to tell the owner of (the bell). <c>id</c> is <c>service:failed:{serviceId}:{yyyy-MM-dd}</c> (the India date of the failed run): one notice per service per day, with the text and time of the latest failure. <c>kind</c> is service_failed; <c>severity</c> error.</summary>
public sealed record ServiceNotice(string Id, string Kind, string Severity, string Text, string ServiceId, DateTimeOffset? At);

public sealed record ServiceNotices(IReadOnlyList<ServiceNotice> Items);

/// <summary>The limits of the run history.</summary>
public static class ServiceRunLimits
{
    /// <summary>The most runs of a service the history shows (older ones stay in the table; nothing is deleted).</summary>
    public const int MaxShown = 200;

    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;
}
