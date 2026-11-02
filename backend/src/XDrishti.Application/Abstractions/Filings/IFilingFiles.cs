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
    /// Opens the stored copy for reading, forward only (P15-03). The hash of the stored file is checked first: a different hash is <c>content_changed</c>, a missing or unreadable file <c>unreadable</c>. Read-only; nothing is created, changed or deleted.
    /// </summary>
    ValueTask<Result<Stream>> OpenAsync(string sha256, CancellationToken cancellationToken);
}
