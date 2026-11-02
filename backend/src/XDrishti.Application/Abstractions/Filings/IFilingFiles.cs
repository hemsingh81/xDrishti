using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>The store folder <c>data/filings/files/&lt;sha[0..2]&gt;/&lt;sha&gt;</c> (P15-02): the only copy of a filing. Files are created 0600 in 0700 folders; the app never deletes a filing or a file.</summary>
public interface IFilingFiles
{
    /// <summary>The bytes held in the store folder.</summary>
    ValueTask<long> UsedBytesAsync(CancellationToken cancellationToken);

    /// <summary>The free bytes of the volume, or null when they cannot be read.</summary>
    long? FreeBytes();

    /// <summary>
    /// Copies the stream into the store under its hash and re-hashes the copy. Returns whether a file was created (false when the same bytes were already there). Fails with <c>content_changed</c> (the source did not hash to <paramref name="sha256"/>), <c>unreadable</c> or
    /// <c>store_conflict</c> (a different file sits at that name; nothing is replaced).
    /// </summary>
    ValueTask<Result<bool>> CopyAsync(Stream source, string sha256, long expectedBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="CopyAsync"/> for bytes that can be rebuilt from the inbox file they came from (the text of one announcement row, P15-07): no flush to disk per file and no second read of the copy, and a file at that name that does not hash to its name (a copy the machine lost
    /// before it reached the disk) is replaced by the right bytes. Nothing else is replaced. A rerun of the loader repairs what a crash lost, because the copy is made before the row is stored.
    /// </summary>
    ValueTask<Result<bool>> CopyRebuildableAsync(Stream source, string sha256, long expectedBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the stored copy for reading, forward only (P15-03). The hash of the stored file is checked first: a different hash is <c>content_changed</c>, a missing or unreadable file <c>unreadable</c>. Read-only; nothing is created, changed or deleted.
    /// </summary>
    ValueTask<Result<Stream>> OpenAsync(string sha256, CancellationToken cancellationToken);
}
