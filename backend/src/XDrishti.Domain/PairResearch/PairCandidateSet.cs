namespace XDrishti.Domain.PairResearch;

/// <summary>The candidates of one formation and what was left out, counted: members with no sector, correlation that could not be computed, correlation under the minimum and pairs cut by the cap.</summary>
public sealed record PairCandidateSet(IReadOnlyList<PairCandidate> Candidates, int SameSectorPairs, int NoSector, int CorrelationUnavailable, int BelowCorrelation, int Capped);
