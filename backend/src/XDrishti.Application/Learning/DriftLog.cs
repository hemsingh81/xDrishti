using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the drift replay (P6-09; ids 4560 and 4561). Counts, codes and milliseconds only: never a trade.</summary>
internal static partial class DriftLog
{
    [LoggerMessage(EventId = 4560, Level = LogLevel.Information, Message = "learning.drift.completed {Cells} cells, {Events} events, {Proposals} proposals, total {TotalMs} ms")]
    public static partial void Completed(ILogger logger, int cells, int events, int proposals, long totalMs);

    [LoggerMessage(EventId = 4561, Level = LogLevel.Warning, Message = "learning.drift.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
