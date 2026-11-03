namespace XDrishti.Domain.PairResearch;

/// <summary>What a stored formation says for the evidence block: the counts and the half-lives (in sessions) of its selected pairs.</summary>
public sealed record PairFormationStat(string Quarter, int Candidates, int Tested, int Unavailable, int Selected, IReadOnlyList<double> HalfLives);
