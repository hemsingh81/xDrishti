using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>
/// A place filings come from (P15-01). The only adapter reads a local inbox folder; it never writes, moves or deletes there, and nothing here reaches a network. File bytes are untrusted data and are never parsed by the source.
/// </summary>
public interface IFilingsSource
{
    /// <summary>Lists the accepted and the rejected files in a fixed order. A missing or loose inbox is a problem in the listing, not an error.</summary>
    ValueTask<FilingListing> ListAsync(FilingQuery query, CancellationToken cancellationToken);

    /// <summary>Opens a listed file read-only. Reading to the end re-verifies the SHA-256 and throws <see cref="FilingContentChangedException"/> (<c>content_changed</c>) when it differs. A file that is gone, not regular or no longer the listed size is a failure with code <c>unreadable</c>.</summary>
    ValueTask<Result<Stream>> OpenAsync(FilingRef filing, CancellationToken cancellationToken);
}
