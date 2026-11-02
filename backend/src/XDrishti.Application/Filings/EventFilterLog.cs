using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Filings;

/// <summary>Structured log events of the event-risk filter (event ids 6998 to 7006, P15-06): counts, ids and closed codes only. Never a password, a reason text or a file.</summary>
internal static partial class EventFilterLog
{
    [LoggerMessage(EventId = 6998, Level = LogLevel.Information, Message = "selection.event_filter.switched decision={Id} enabled={Enabled} results_within_days={Days} approval={Approval} decided_by={DecidedBy}")]
    public static partial void Switched(ILogger logger, long id, bool enabled, int days, string approval, string decidedBy);

    [LoggerMessage(EventId = 6999, Level = LogLevel.Information, Message = "selection.event_filter.switch_refused code={Code}")]
    public static partial void SwitchRefused(ILogger logger, string code);

    [LoggerMessage(EventId = 7000, Level = LogLevel.Information, Message = "selection.event_filter.auto_applied decision={Id} action={Action} reason={Reason}")]
    public static partial void AutoApplied(ILogger logger, long id, string action, string reason);

    [LoggerMessage(EventId = 7001, Level = LogLevel.Information, Message = "selection.event_filter.auto_not_applied reason={Reason}")]
    public static partial void AutoNotApplied(ILogger logger, string reason);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Information, Message = "selection.event_filter.excluded for_date={ForDate} days={Days} excluded={Excluded} kept={Kept}")]
    public static partial void Excluded(ILogger logger, DateOnly forDate, int days, int excluded, int kept);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Information, Message = "selection.event_filter.unavailable for_date={ForDate} instruments={Instruments} reason={Reason}")]
    public static partial void Unavailable(ILogger logger, DateOnly forDate, int instruments, string reason);

    [LoggerMessage(EventId = 7004, Level = LogLevel.Warning, Message = "selection.event_filter.audit_failed failure={Failure}")]
    public static partial void AuditFailed(ILogger logger, string failure);

    [LoggerMessage(EventId = 7006, Level = LogLevel.Warning, Message = "selection.event_filter.verdict_read_failed failure={Failure}")]
    public static partial void VerdictReadFailed(ILogger logger, string failure);

    [LoggerMessage(EventId = 7005, Level = LogLevel.Warning, Message = "selection.event_filter.calendar_read_failed failure={Failure}")]
    public static partial void CalendarReadFailed(ILogger logger, string failure);
}
