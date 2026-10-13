namespace XDrishti.Domain.Platform.Services;

/// <summary>
/// One finding of the schedule checks (ADR 0018 §6): which service and field it is about (the field is a dotted path like the request, for example <c>schedule.time</c>,
/// <c>retries</c> or <c>parameters.throttle</c>), how serious it is, a stable <see cref="Code"/> and the text. <see cref="RelatedServiceId"/> names the other service when the
/// finding comes from two of them (the same minute, an order that is wrong).
/// </summary>
public sealed record ScheduleIssue(string ServiceId, string Field, IssueSeverity Severity, string Code, string Message, string? RelatedServiceId = null)
{
    public bool IsError => Severity == IssueSeverity.Error;

    public static ScheduleIssue Error(string serviceId, string field, string code, string message, string? related = null) =>
        new(serviceId, field, IssueSeverity.Error, code, message, related);

    public static ScheduleIssue Warning(string serviceId, string field, string code, string message, string? related = null) =>
        new(serviceId, field, IssueSeverity.Warning, code, message, related);
}
