namespace XDrishti.Application.Abstractions.Research;

/// <summary>What an append did: the version the content has and whether a row was written (false: the same content was already stored and nothing changed).</summary>
public sealed record AppendResult(string StrategyId, int Version, string Hash, bool Created);
