namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>What undoing the load of a CSV file removed: the instrument, the range that was worked on (the file's recorded first and last bar) and how many CSV bars went.</summary>
public sealed record CsvUndoResult(Guid InstrumentId, DateTimeOffset From, DateTimeOffset To, long BarsRemoved);

/// <summary>
/// Undoes the load of one CSV file (ADR 0017 §6). The application role has no DELETE on bars, so the removal is one narrow database function (<c>market.undo_csv_bars</c>) that deletes only the
/// <c>csv</c>-source bars of one instrument within a range, never Dhan bars. Values an earlier load overwrote are not restored.
/// </summary>
public interface ICsvUndo
{
    /// <summary>
    /// In one transaction: removes the CSV bars of the file's instrument within the file's recorded first-to-last bar range (the instrument's whole range for a file loaded before the range was recorded),
    /// widens the instrument's changed range so the aggregates are rebuilt, and takes the file off the loaded list (it is pending again). Null when the file is not loaded (never, or undone already).
    /// </summary>
    ValueTask<CsvUndoResult?> UndoAsync(string fileName, DateTimeOffset now, CancellationToken cancellationToken);
}
