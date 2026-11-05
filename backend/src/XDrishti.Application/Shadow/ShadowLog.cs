using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Shadow;

/// <summary>Structured log events of the shadow record (CF-08; ids 5400 to 5409). Counts only: no level, no feature, no symbol, no strategy text.</summary>
internal static partial class ShadowLog
{
    [LoggerMessage(EventId = 5400, Level = LogLevel.Information, Message = "shadow.write: {Sessions} session(s), {Rows} row(s), {EdgeRows} counting for edge, {Gaps} gap(s), {Dropped} left out for a same trade key")]
    public static partial void Written(ILogger logger, int sessions, int rows, int edgeRows, int gaps, int dropped);

    [LoggerMessage(EventId = 5401, Level = LogLevel.Information, Message = "shadow.write: switched off, nothing written")]
    public static partial void SwitchedOff(ILogger logger);
}
