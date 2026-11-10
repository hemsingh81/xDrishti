using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>How the new stays of a list basket are labelled (BK-05): the provenance, the kind of the source record and the note (ASM/GSM category) per instrument. Without it the stays are <c>fetched</c> by <c>fetch</c> (index lists).</summary>
public sealed record ListOrigin(string Provenance, string SourceKind, IReadOnlyDictionary<Guid, string?> Notes, Guid SourceRef);

/// <summary>What to apply: the diff, the effective instant (the start of the India date of the fetch, or of the trade date a file names) and everything that is stored with it.</summary>
public sealed record MembershipApplication(
    Guid BasketId, string BasketName, MembershipChange Change, IReadOnlyList<Guid> IncomingOrder, DateTimeOffset EffectiveFrom, DateTimeOffset DetectedAt, SourceUpdate Source, FetchRecord Fetch, ListOrigin? Origin = null);

/// <summary>Applies a membership change in ONE transaction: ends the stays of the removed instruments, adds the new ones (provenance fetched), writes the fetch row, the source state and the audit event (counts only). All or nothing. False when a concurrent change got in first (nothing was written).</summary>
public interface IBasketMembershipApplier
{
    ValueTask<bool> ApplyAsync(MembershipApplication application, CancellationToken cancellationToken);
}
