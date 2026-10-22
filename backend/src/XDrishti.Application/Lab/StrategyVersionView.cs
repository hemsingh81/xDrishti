namespace XDrishti.Application.Lab;

/// <summary>One stored version as the library shows it (spec P5-01): the text exactly as it was posted, its hash and lineage, and whether the text is free of errors under the rules of today.</summary>
public sealed record StrategyVersionView(string Id, int Version, string Hash, string? ParentHash, string? Note, DateTimeOffset CreatedAt, string Text, bool ProblemsFree);
