using Microsoft.Extensions.Logging;

namespace XDrishti.Application.ChargeSheet;

/// <summary>Structured log events of the charge-sheet confirmation (P8-23; ids 4950 to 4953). Row ids, kinds and error codes only: never a reason, an actor, a sheet id or a hash.</summary>
internal static partial class ChargeSheetLogEvents
{
    [LoggerMessage(EventId = 4950, Level = LogLevel.Information, Message = "chargesheet.act {RowId}: {Kind}")]
    public static partial void Recorded(ILogger logger, long rowId, string kind);

    [LoggerMessage(EventId = 4951, Level = LogLevel.Information, Message = "chargesheet.act.repeated {Kind}: the log already said it, nothing written")]
    public static partial void Repeated(ILogger logger, string kind);

    [LoggerMessage(EventId = 4952, Level = LogLevel.Warning, Message = "chargesheet.refused {Kind}: {Code}")]
    public static partial void Refused(ILogger logger, string kind, string code);
}
