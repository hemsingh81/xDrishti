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

    [LoggerMessage(EventId = 5135, Level = LogLevel.Warning, Message = "live.ramp_hold event={EventId} step={Step}")]
    public static partial void HoldWritten(ILogger logger, long eventId, int step);

    [LoggerMessage(EventId = 5136, Level = LogLevel.Warning, Message = "live.ramp_step_down event={EventId} from={From} to={To}")]
    public static partial void StepDownWritten(ILogger logger, long eventId, int from, int to);

    [LoggerMessage(EventId = 5137, Level = LogLevel.Information, Message = "live.ramp_auto_step_up event={EventId} from={From} to={To}")]
    public static partial void AutoStepUpWritten(ILogger logger, long eventId, int from, int to);

    [LoggerMessage(EventId = 5138, Level = LogLevel.Warning, Message = "live.halt_opened id={Id} kind={Kind} action={Action} state={State}")]
    public static partial void HaltOpened(ILogger logger, long id, string kind, string action, string state);

    [LoggerMessage(EventId = 5139, Level = LogLevel.Warning, Message = "live.ramp_evaluation_failed type={ErrorType}")]
    public static partial void EvaluationFailed(ILogger logger, string errorType);
}
