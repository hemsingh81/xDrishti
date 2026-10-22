using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Research;

/// <summary>
/// Structured log events of the exit-choice store (spec P4-06, D10; event ids 4428 to 4430, unique across the solution, asserted by the architecture tests). The selection key (first 12 characters), the cell, counts, the chosen candidate id, codes and
/// milliseconds only: never a trade, a table or the record text.
/// </summary>
internal static partial class ExitSelectionStoreLog
{
    [LoggerMessage(EventId = 4428, Level = LogLevel.Information, Message = "store.exit_selection.completed selection {SelectionKey} for {Cell}: {Folds} fold rows, chosen {Chosen}, stable {Stable}, {Outcome}, total {TotalMs} ms")]
    public static partial void Completed(ILogger logger, string selectionKey, string cell, int folds, string chosen, string stable, string outcome, long totalMs);

    [LoggerMessage(EventId = 4429, Level = LogLevel.Error, Message = "store.exit_selection.conflict selection {SelectionKey}: another record is stored under this key; differing figures: {Differing}")]
    public static partial void Conflict(ILogger logger, string selectionKey, string differing);

    [LoggerMessage(EventId = 4430, Level = LogLevel.Error, Message = "store.exit_selection.failed selection {SelectionKey}: {Code}")]
    public static partial void Failed(ILogger logger, string selectionKey, string code);
}
