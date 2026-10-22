namespace XDrishti.Application.Lab;

/// <summary>What a create did (spec P5-01): the version the content has, its hash and parent, and whether a row was written (false: the same content was already stored).</summary>
public sealed record CreatedStrategy(string Id, int Version, string Hash, string? ParentHash, bool Created);
