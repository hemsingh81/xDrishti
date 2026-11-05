namespace XDrishti.Domain.Shadow;

/// <summary>A stored session of the chain with its stored rows.</summary>
/// <param name="SessionDate">The session.</param>
/// <param name="Status"><c>ok</c> or <c>gap</c>.</param>
/// <param name="PrevDigest">The previous session's digest as stored, null for the first session.</param>
/// <param name="Digest">The digest as stored.</param>
/// <param name="Entries">The rows of the session as stored; none for a gap.</param>
public sealed record ShadowChainSession(DateOnly SessionDate, string Status, string? PrevDigest, string Digest, IReadOnlyList<StoredShadowEntry> Entries);
