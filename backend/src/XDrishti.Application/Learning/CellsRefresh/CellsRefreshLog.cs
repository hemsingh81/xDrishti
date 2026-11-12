using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning.CellsRefresh;

/// <summary>Structured log events of the scheduled cell-statistics refresh (LC-01; ids 4480 and 4481). The snapshot key (first 12 characters), counts and codes only.</summary>
internal static partial class CellsRefreshLog
{
    [LoggerMessage(EventId = 4480, Level = LogLevel.Information, Message = "learning.cells_refresh.completed snapshot {SnapshotKey}: {Kept} source(s) kept, {Extended} simulated again, {NewRuns} new run(s)")]
    public static partial void Completed(ILogger logger, string snapshotKey, int kept, int extended, int newRuns);

    [LoggerMessage(EventId = 4481, Level = LogLevel.Warning, Message = "learning.cells_refresh.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
