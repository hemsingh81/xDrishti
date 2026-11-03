namespace XDrishti.Domain.PairResearch;

/// <summary>The formation window of a quarter: the sessions (empty when there are too few), the members that have every one of them, the number left out for a gap, and how many instruments had any bar before the quarter.</summary>
public sealed record PairWindowResult(IReadOnlyList<DateOnly> Sessions, IReadOnlyList<PairMember> Members, int Incomplete, int Covered, int SessionsAvailable)
{
    /// <summary>True when the quarter has fewer than the required sessions before it.</summary>
    public bool TooFewSessions => Sessions.Count == 0;
}
