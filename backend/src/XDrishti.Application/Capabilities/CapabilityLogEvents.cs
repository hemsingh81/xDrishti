using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Capabilities;

/// <summary>Structured log events of the capability gate (P8-18 Part A; ids 4900 to 4904). Row ids, names and error codes only: never a reason or an actor.</summary>
internal static partial class CapabilityLogEvents
{
    [LoggerMessage(EventId = 4900, Level = LogLevel.Information, Message = "capability.switched {RowId}: {Name} {State}")]
    public static partial void Switched(ILogger logger, long rowId, string name, string state);

    [LoggerMessage(EventId = 4901, Level = LogLevel.Information, Message = "capability.repeated {Name}: the log already said it, nothing written")]
    public static partial void Repeated(ILogger logger, string name);

    [LoggerMessage(EventId = 4902, Level = LogLevel.Warning, Message = "capability.refused {Name}: {Code}")]
    public static partial void Refused(ILogger logger, string name, string code);
}
