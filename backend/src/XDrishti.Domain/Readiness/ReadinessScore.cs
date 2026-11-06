namespace XDrishti.Domain.Readiness;

/// <summary>
/// The readiness score of one strategy (spec CF-11 D2 to D4): the minimum of the applicable sub-scores, or 0 with every hard fail named.
/// </summary>
/// <param name="Score">0 to 100; 0 when any hard fail stands.</param>
/// <param name="RawMinimum">The minimum of the scored sub-scores before the hard fails (0 when none is scored); shown so the owner sees how far the strategy is without the fault.</param>
/// <param name="Label">The label of <see cref="Score"/>.</param>
/// <param name="SubScores">All six, in order, with their state.</param>
/// <param name="HardFails">Each hard fail by name; empty when none.</param>
/// <param name="NotApplicable">The terms and sub-scores left out because they do not apply, named.</param>
/// <param name="Weakest">The weakest sub-score: an unavailable one first, else the lowest scored; null when none.</param>
public sealed record ReadinessScore(double Score, double RawMinimum, ReadinessLabel Label, IReadOnlyList<SubScore> SubScores, IReadOnlyList<string> HardFails, IReadOnlyList<string> NotApplicable, SubScore? Weakest);
