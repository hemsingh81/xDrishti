namespace XDrishti.Domain.Shadow;

/// <summary>A shadow row as the database holds it: the entry rebuilt from the stored columns and the hash that was stored with it.</summary>
/// <param name="Entry">The entry rebuilt from the stored columns.</param>
/// <param name="StoredHash">The <c>row_hash</c> column.</param>
public sealed record StoredShadowEntry(ShadowEntry Entry, string StoredHash);
