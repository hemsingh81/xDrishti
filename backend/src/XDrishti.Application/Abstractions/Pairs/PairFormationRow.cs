namespace XDrishti.Application.Abstractions.Pairs;

/// <summary>A row of <c>research.pair_formations</c>.</summary>
public sealed record PairFormationRow(string FormationId, string Family, string Quarter, DateOnly WindowFrom, DateOnly WindowTo, int Candidates, int Tested, int Unavailable, int Selected, string DetailJson);
