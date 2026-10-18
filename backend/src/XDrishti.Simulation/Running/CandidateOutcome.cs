using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Running;

/// <summary>
/// One entry candidate and what its walk gave: a trade, or the reason there is none (<see cref="WalkOutcome.NoTrade"/>). Kept whole, so that the walker's counters can be summed from the outcomes
/// (<see cref="WalkOutcome.ToCounters"/>) and a candidate that made no trade is still in the result.
/// </summary>
/// <param name="Candidate">The decision of the scanner.</param>
/// <param name="Walk">The outcome of the walk of that decision.</param>
public sealed record CandidateOutcome(EntryCandidate Candidate, WalkOutcome Walk);
