using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Autonomy;

/// <summary>Structured log events of the autonomy settings and auto approvals (P7-12; ids 4730 to 4734). Ids, counts, flags and error codes only: never a reason or an actor.</summary>
internal static partial class AutonomyLog
{
    [LoggerMessage(EventId = 4730, Level = LogLevel.Information, Message = "autonomy.settings.changed {SettingsId}: auto approve {AutoApprove}, promote from reserve {Promote}")]
    public static partial void SettingsChanged(ILogger logger, long settingsId, bool autoApprove, bool promote);

    [LoggerMessage(EventId = 4731, Level = LogLevel.Information, Message = "autonomy.auto.pass {PlanId}: {Approved} approved by rule, {Declined} left for the owner")]
    public static partial void Pass(ILogger logger, long planId, int approved, int declined);

    [LoggerMessage(EventId = 4732, Level = LogLevel.Information, Message = "autonomy.auto.skipped {PlanId}: the switch is off, manual approval")]
    public static partial void SkippedOff(ILogger logger, long planId);

    [LoggerMessage(EventId = 4733, Level = LogLevel.Information, Message = "autonomy.auto.approved {ItemId} under settings {SettingsId}")]
    public static partial void Approved(ILogger logger, long itemId, long settingsId);

    [LoggerMessage(EventId = 4734, Level = LogLevel.Warning, Message = "autonomy.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
