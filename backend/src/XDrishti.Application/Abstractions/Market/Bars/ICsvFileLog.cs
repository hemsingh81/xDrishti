namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>
/// A CSV file that was loaded: what it looked like (name, size, modified time) and what loading it did. <paramref name="FirstBar"/> and <paramref name="LastBar"/> are the first and last bar
/// start the file held (null for a file loaded before they were recorded; the undo then falls back to the instrument's range).
/// </summary>
public sealed record LoadedCsvFile(
    string Name,
    long Size,
    DateTimeOffset ModifiedAt,
    Guid? InstrumentId,
    long Rows,
    long Inserted,
    long Updated,
    long Repaired,
    long Rejected,
    DateTimeOffset LoadedAt,
    DateTimeOffset? FirstBar = null,
    DateTimeOffset? LastBar = null);

/// <summary>
/// The files loaded so far (and not undone since). A file is written in the same transaction as its bars (<see cref="IBarMergeSession.CompleteAsync"/>), so a file is listed only if its bars are stored;
/// undoing a load (<see cref="ICsvUndo"/>) takes it off the list again.
/// </summary>
public interface ICsvFileLog
{
    ValueTask<IReadOnlyDictionary<string, LoadedCsvFile>> ListAsync(CancellationToken cancellationToken);
}
