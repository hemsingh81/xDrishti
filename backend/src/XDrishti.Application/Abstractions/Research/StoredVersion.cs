namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// One stored version of a strategy (spec P3-02d, D2/D4): the text as it was posted or seeded (byte for byte), the canonical text that was hashed, the hash (unique: the same content is one row) and the lineage. A version is never changed.
/// </summary>
public sealed record StoredVersion(
    string StrategyId,
    int Version,
    int SchemaVersion,
    string Hash,
    string SourceText,
    string CanonicalJson,
    string? ParentHash,
    string? Note,
    DateTimeOffset CreatedAt);
