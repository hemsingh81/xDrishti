using Microsoft.Extensions.Logging;

namespace XDrishti.Application.LiveTrading;

/// <summary>Structured log events of the ramp (event ids 5130 to 5139). Fixed words, ids and codes only - never a reason text or a password.</summary>
internal static partial class RampLog
{
    [LoggerMessage(EventId = 5130, Level = LogLevel.Information, Message = "live.ramp_step_set event={EventId} from={From} to={To}")]
    public static partial void StepSet(ILogger logger, long eventId, int? from, int to);

    [LoggerMessage(EventId = 5131, Level = LogLevel.Warning, Message = "live.ramp_refused code={Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 5132, Level = LogLevel.Information, Message = "live.settings_changed id={Id} autoApprove={AutoApprove} autoRampUp={AutoRampUp}")]
    public static partial void SettingsChanged(ILogger logger, long id, bool autoApprove, bool autoRampUp);

    [LoggerMessage(EventId = 5133, Level = LogLevel.Warning, Message = "live.audit_failed type={ErrorType}: the row stands, its audit event was not written")]
    public static partial void AuditFailed(ILogger logger, string errorType);
}
