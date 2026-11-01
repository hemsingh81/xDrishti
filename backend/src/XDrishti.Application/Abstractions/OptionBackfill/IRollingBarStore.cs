namespace XDrishti.Application.Abstractions.OptionBackfill;

/// <summary>
/// The write side of <c>options.rolling_bars</c> and the measures of the disk guard (P14-03). <b>There is no method that reads a bar</b> (an architecture test keeps it so): nothing reads the backfilled data in this slice. Bars only go in, with
/// <c>ON CONFLICT DO NOTHING</c>; nothing is updated or deleted (the runtime role cannot).
/// </summary>
public interface IRollingBarStore
{
    /// <summary>Writes the attempt's fetch-log row and its bars in one transaction; returns how many bars were new.</summary>
    ValueTask<int> AppendAsync(BackfillFetchRecord attempt, IReadOnlyList<BackfillBar> bars, CancellationToken cancellationToken);

    /// <summary>The stored size of <c>options.rolling_bars</c> in bytes (<c>hypertable_detailed_size</c>).</summary>
    ValueTask<long> StoredBytesAsync(CancellationToken cancellationToken);

    /// <summary>The free bytes of the file system this process runs on; null when they cannot be read.</summary>
    ValueTask<long?> FreeBytesAsync(CancellationToken cancellationToken);
}
