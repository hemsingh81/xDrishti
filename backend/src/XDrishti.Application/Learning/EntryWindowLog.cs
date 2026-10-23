using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the learned entry windows (P6-14; ids 4510 to 4512). Counts, codes and milliseconds only: never a trade.</summary>
internal static partial class EntryWindowLog
{
    [LoggerMessage(EventId = 4510, Level = LogLevel.Information, Message = "learning.windows.completed {Cells} cells, {Proposed} proposed, {Rows} rows, {Folds} folds, total {TotalMs} ms")]
    public static partial void Completed(ILogger logger, int cells, int proposed, int rows, int folds, long totalMs);

    [LoggerMessage(EventId = 4511, Level = LogLevel.Warning, Message = "learning.windows.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
