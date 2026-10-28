using Microsoft.Extensions.Logging;

namespace XDrishti.Application.LiveTrading;

/// <summary>Structured log events of the live-trading records (event ids 5140 to 5149). Fixed words and codes only - never the text of a record.</summary>
internal static partial class LiveLog
{
    [LoggerMessage(EventId = 5140, Level = LogLevel.Information, Message = "live.compliance_recorded id={Id} kind={Kind} outcome={Outcome}")]
    public static partial void ComplianceRecorded(ILogger logger, long id, string kind, string outcome);

    [LoggerMessage(EventId = 5141, Level = LogLevel.Warning, Message = "live.compliance_refused code={Code}")]
    public static partial void ComplianceRefused(ILogger logger, string code);

    [LoggerMessage(EventId = 5142, Level = LogLevel.Warning, Message = "live.compliance_audit_failed type={ErrorType}: the record stands, its audit event was not written")]
    public static partial void ComplianceAuditFailed(ILogger logger, string errorType);
}
