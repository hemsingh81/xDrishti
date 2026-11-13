using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Shadow;

/// <summary>The frozen evidence set of the current edge epoch of the shadow record (spec AM-01 GAP-09, ADR 0282). Read only; no parameters.</summary>
public sealed record GetEvidenceSetQuery : IQuery<EvidenceSetView>;

/// <summary>
/// The evidence set of the current edge epoch: the strategy versions whose shadow rows count for edge and for the readiness forward rows, and who or what froze it.
/// </summary>
/// <param name="EdgeEpoch">The paper epoch the set belongs to, or null while no paper epoch exists.</param>
/// <param name="State"><c>pending</c> (no set stored: the first shadow write of the epoch registers the default), <c>open</c> (a set is stored, no shadow row of the epoch yet: the owner may still replace it) or <c>frozen</c> (a shadow row exists: never replaceable).</param>
/// <param name="Versions">The versions of the stored set; for <c>pending</c> the versions the default rule would register now (empty when none is eligible).</param>
/// <param name="SetHash">The identity of the stored set, or null while pending.</param>
/// <param name="Mode"><c>auto</c> (the system default rule), <c>owner</c>, or <c>backfill</c> (written by the AM-01b migration for rows that already existed: not a pre-registration); null while pending.</param>
/// <param name="Actor"><c>system:default</c>, <c>system:migration</c> (a backfill) or the owner's actor; null while pending.</param>
/// <param name="Reason">The owner's reason; null for an automatic set or while pending.</param>
/// <param name="CreatedAt">When the stored set was written; null while pending.</param>
/// <param name="FirstWriteAt">When the epoch's first shadow write was stored with the automatic set; null otherwise.</param>
/// <param name="ShadowEnabled">Whether the switch <c>paper.shadow.enabled</c> is ON.</param>
/// <param name="Note">What the state means and what happens next, in words.</param>
public sealed record EvidenceSetView(long? EdgeEpoch, string State, IReadOnlyList<string> Versions, string? SetHash, string? Mode, string? Actor, string? Reason, DateTimeOffset? CreatedAt, DateTimeOffset? FirstWriteAt, bool ShadowEnabled, string Note);
