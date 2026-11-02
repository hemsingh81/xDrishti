using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>The stored announcements the sampler may draw: the dated ones that have a <c>filings.announcements</c> row, and how many announcement envelopes have none (<see cref="NoMetadata"/>: a text or pdf file with one sidecar time; never sampled).</summary>
public sealed record AnnouncementCandidates(IReadOnlyList<SampleCandidate> Dated, int NoMetadata);

/// <summary>Reads stored announcements for the sample (P15-07). Read only.</summary>
public interface IAnnouncementCandidates
{
    /// <summary>The state-ok announcements broadcast in [<paramref name="from"/>, <paramref name="toExclusive"/>) with their symbol and exchange category.</summary>
    ValueTask<AnnouncementCandidates> ListAsync(DateTimeOffset from, DateTimeOffset toExclusive, CancellationToken cancellationToken);

    /// <summary>The category, subject and text of the named announcements, read from their stored files; an id whose file is missing or changed is absent from the answer.</summary>
    ValueTask<IReadOnlyDictionary<Guid, AnnouncementBody>> ReadBodiesAsync(IReadOnlyCollection<Guid> filingIds, CancellationToken cancellationToken);
}
