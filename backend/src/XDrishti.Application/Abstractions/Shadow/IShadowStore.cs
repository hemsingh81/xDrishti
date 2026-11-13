using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Abstractions.Shadow;

/// <summary>The newest session of the chain.</summary>
/// <param name="SessionDate">The session.</param>
/// <param name="Digest">Its digest.</param>
public sealed record ShadowHead(DateOnly SessionDate, string Digest);

/// <summary>A session to store: its chain link and, for an <c>ok</c> session, its rows. A <c>gap</c> has none.</summary>
/// <param name="SessionDate">The session.</param>
/// <param name="Status"><c>ok</c> or <c>gap</c>.</param>
/// <param name="PrevDigest">The digest of the newest stored session, null for the first.</param>
/// <param name="Digest">The digest of this session (<see cref="ShadowDigest.Next"/>).</param>
/// <param name="Entries">The rows.</param>
/// <param name="Scores">The models' scores of the rows (CF-10e part 10e-1), stored in the same transaction; none by default.</param>
/// <param name="EvidenceSet">The automatic evidence set of the epoch's first session (spec AM-01 GAP-09): stored, with its ledger rows, before the entries in the same transaction; null when the epoch already has a set.</param>
public sealed record NewShadowSession(DateOnly SessionDate, string Status, string? PrevDigest, string Digest, IReadOnlyList<ShadowEntry> Entries, IReadOnlyList<NewShadowScore>? Scores = null, NewEvidenceSet? EvidenceSet = null);

/// <summary>
/// The shadow record (<c>paper.shadow_entries</c>, <c>shadow_sessions</c>; spec CF-08, ADR 0211) behind a port. Forward-only and insert-only: the only write stores a whole session in one transaction, and the database refuses a session that is opened,
/// a row stamped after the open and a broken chain. No member updates, deletes or back-fills.
/// </summary>
public interface IShadowStore
{
    /// <summary>The newest stored session, or null before the first.</summary>
    ValueTask<ShadowHead?> HeadAsync(CancellationToken cancellationToken);

    /// <summary>Stores the session and its rows in one transaction; false (and nothing stored) when a session of that date is stored already.</summary>
    ValueTask<bool> WriteSessionAsync(NewShadowSession session, CancellationToken cancellationToken);

    /// <summary>Every stored session with its rows as stored, oldest first, for the chain check. Read only.</summary>
    ValueTask<IReadOnlyList<ShadowChainSession>> ReadChainAsync(CancellationToken cancellationToken);
}
