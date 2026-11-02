using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>Metadata reads of the filing store for the two routes (P15-02): counts and envelope fields only, never a file.</summary>
public interface IFilingReader
{
    ValueTask<FilingCoverageData> CoverageAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Envelopes, newest broadcast first (unavailable ones last), at most <paramref name="limit"/>; one extra row is read so the caller can tell the list is cut.</summary>
    ValueTask<IReadOnlyList<FilingView>> ListAsync(Guid? instrumentId, FilingKind? kind, int limit, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<FilingView>> ByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    ValueTask<FilingView?> FindAsync(Guid id, CancellationToken cancellationToken);
}
