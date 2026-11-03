using XDrishti.Domain.PairResearch;

namespace XDrishti.Application.Abstractions.Pairs;

/// <summary>A stored formation as the evaluation reads it: its id, when it was stored and its counts and half-lives.</summary>
public sealed record StoredFormation(string FormationId, DateTimeOffset CreatedAt, PairFormationStat Stat);
