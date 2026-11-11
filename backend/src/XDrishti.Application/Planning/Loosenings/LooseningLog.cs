using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Planning;

/// <summary>Structured log events of the loosening banner (BK-10b; ids 7450 to 7459). Sources, counts and actor kinds only: never a value, a password or an e-mail address.</summary>
internal static partial class LooseningLog
{
    [LoggerMessage(EventId = 7450, Level = LogLevel.Information, Message = "loosening.dismissed {Source} {NoticeId} by {ActorKind}")]
    public static partial void Dismissed(ILogger logger, string source, long noticeId, string actorKind);

    [LoggerMessage(EventId = 7451, Level = LogLevel.Information, Message = "loosening.auto_dismissed {Count} for plan {PlanId}")]
    public static partial void AutoDismissed(ILogger logger, int count, long planId);

    [LoggerMessage(EventId = 7452, Level = LogLevel.Warning, Message = "loosening.auto_dismiss_failed plan {PlanId}: {Failure}")]
    public static partial void AutoDismissFailed(ILogger logger, long planId, string failure);

    [LoggerMessage(EventId = 7453, Level = LogLevel.Warning, Message = "loosening.audit_failed {Failure}")]
    public static partial void AuditFailed(ILogger logger, string failure);

    [LoggerMessage(EventId = 7454, Level = LogLevel.Warning, Message = "loosening.read_failed {Failure}")]
    public static partial void ReadFailed(ILogger logger, string failure);

    [LoggerMessage(EventId = 7455, Level = LogLevel.Information, Message = "loosening.setting_changed auto {Auto}, version {Version}")]
    public static partial void SettingChanged(ILogger logger, bool auto, long version);

    [LoggerMessage(EventId = 7456, Level = LogLevel.Warning, Message = "loosening.setting_refused {Code}")]
    public static partial void SettingRefused(ILogger logger, string code);
}
