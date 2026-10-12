namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>A CSV file that was loaded: what it looked like (name, size, modified time) and what loading it did.</summary>
public sealed record LoadedCsvFile(string Name, long Size, DateTimeOffset ModifiedAt, Guid? InstrumentId, long Rows, long Inserted, long Updated, long Repaired, long Rejected, DateTimeOffset LoadedAt);

/// <summary>The files loaded so far. A file is written in the same transaction as its bars (<see cref="IBarMergeSession.CompleteAsync"/>), so a file is listed only if its bars are stored.</summary>
public interface ICsvFileLog
{
    ValueTask<IReadOnlyDictionary<string, LoadedCsvFile>> ListAsync(CancellationToken cancellationToken);
}
