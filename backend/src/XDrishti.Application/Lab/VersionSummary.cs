namespace XDrishti.Application.Lab;

/// <summary>A stored version in the list of a family: number, hash, parent hash, note and creation time.</summary>
public sealed record VersionSummary(int Version, string Hash, string? ParentHash, string? Note, DateTimeOffset CreatedAt);
