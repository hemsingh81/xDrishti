using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the cell statistics (P6-01; ids 4470 to 4473). The snapshot key (first 12 characters), counts, codes and milliseconds only: never a trade.</summary>
internal static partial class CellStatsLog
{
    [LoggerMessage(EventId = 4470, Level = LogLevel.Information, Message = "learning.cells.completed snapshot {SnapshotKey} as of {AsOf}: {Cells} cells, {Rows} rows from {Trades} trades, {Outcome}, total {TotalMs} ms")]
    public static partial void Completed(ILogger logger, string snapshotKey, string asOf, int cells, int rows, int trades, string outcome, long totalMs);

    [LoggerMessage(EventId = 4473, Level = LogLevel.Information, Message = "learning.cells.shrinkage snapshot {SnapshotKey}: {ShrunkRows} rows shrunk, {FloorRows} at the tau-squared floor")]
    public static partial void Shrinkage(ILogger logger, string snapshotKey, int shrunkRows, int floorRows);

    [LoggerMessage(EventId = 4474, Level = LogLevel.Information, Message = "learning.cells.universe snapshot {SnapshotKey}: {TierCells} tier cells, {Tiered} trades tiered, {Unknown} unknown, {Unavailable} unavailable")]
    public static partial void Universe(ILogger logger, string snapshotKey, int tierCells, int tiered, int unknown, int unavailable);

    [LoggerMessage(EventId = 4471, Level = LogLevel.Warning, Message = "learning.cells.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 4472, Level = LogLevel.Error, Message = "learning.cells.failed snapshot {SnapshotKey}")]
    public static partial void Failed(ILogger logger, string snapshotKey);
}
