namespace XDrishti.Application.Abstractions.Shadow;

/// <summary>An evidence set as stored (<c>paper.evidence_sets</c>, spec AM-01 GAP-09).</summary>
/// <param name="Id">The row.</param>
/// <param name="EdgeKey">The paper epoch id the set belongs to, or 0 while no paper epoch exists.</param>
/// <param name="Versions">The strategy version hashes, canonical order.</param>
/// <param name="SetHash">The identity of the set.</param>
/// <param name="Mode"><c>auto</c> (the rule, at the epoch's first shadow write) or <c>owner</c> (named before it).</param>
/// <param name="Actor"><c>system:default</c> or <c>owner:...</c>.</param>
/// <param name="Reason">The owner's reason, or null for the automatic set.</param>
/// <param name="CreatedAt">When stored (the database clock).</param>
/// <param name="FirstWriteAt">When the automatic set's first shadow write was stored (the same instant); null for an owner set, whose freeze is read from the rows.</param>
/// <param name="Frozen">Whether a shadow row of the epoch is stored: the set can then never be replaced.</param>
public sealed record StoredEvidenceSet(long Id, long EdgeKey, IReadOnlyList<string> Versions, string SetHash, string Mode, string Actor, string? Reason, DateTimeOffset CreatedAt, DateTimeOffset? FirstWriteAt, bool Frozen);

/// <summary>An evidence set to store.</summary>
/// <param name="EdgeKey">The paper epoch id, or 0 while none exists.</param>
/// <param name="Versions">Canonical version hashes.</param>
/// <param name="SetHash">The identity of the set.</param>
/// <param name="Mode"><c>auto</c> or <c>owner</c>.</param>
/// <param name="Actor">The actor.</param>
/// <param name="Reason">The owner's reason, or null.</param>
public sealed record NewEvidenceSet(long EdgeKey, IReadOnlyList<string> Versions, string SetHash, string Mode, string Actor, string? Reason);

/// <summary>
/// The frozen evidence sets of the shadow record (spec AM-01 GAP-09, ADR 0282) behind a port. Insert-only: the newest row of an epoch is current, so a replace is a new row; the database refuses a row once a shadow entry of the epoch exists. The automatic set is stored
/// by <see cref="IShadowStore"/> in the transaction of the epoch's first session, so it never exists without its first write.
/// </summary>
public interface IEvidenceSetStore
{
    /// <summary>The newest set of the epoch, or null when none is stored.</summary>
    ValueTask<StoredEvidenceSet?> CurrentAsync(long edgeKey, CancellationToken cancellationToken);

    /// <summary>Stores an owner set (and registers its versions in the trial ledger) in one transaction; false and nothing stored when the epoch already has a shadow row.</summary>
    ValueTask<bool> ReplaceAsync(NewEvidenceSet set, CancellationToken cancellationToken);
}
