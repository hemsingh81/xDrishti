using Microsoft.Extensions.Logging;

namespace XDrishti.Application.OwnerActs;

/// <summary>Structured log events of the owner-act forms (P8-19; ids 4920 to 4939). Capability names, event names and codes only: never a reason, an actor, a password or a number of the evidence.</summary>
internal static partial class OwnerActLogEvents
{
    [LoggerMessage(EventId = 4920, Level = LogLevel.Warning, Message = "owneract.refused_off {Capability}: switched off, nothing called")]
    public static partial void RefusedOff(ILogger logger, string capability);

    [LoggerMessage(EventId = 4921, Level = LogLevel.Information, Message = "owneract.done {Event}")]
    public static partial void Done(ILogger logger, string @event);

    [LoggerMessage(EventId = 4922, Level = LogLevel.Error, Message = "owneract.audit_failed {Event}: the move stands, the audit cross-index row was not written")]
    public static partial void AuditFailed(ILogger logger, string @event);
}
