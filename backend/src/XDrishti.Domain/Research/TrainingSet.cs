namespace XDrishti.Domain.Research;

/// <summary>
/// The record of a labelled training set (P6-03, ADR 0065): which runs it came from, the rule that labelled it, the dates and the counts. <see cref="Id"/> is a pure function of the rule, the runs and the labels, so the same inputs give the same id.
/// Persisting it comes with the features (P6-04).
/// </summary>
/// <param name="Id">SHA-256 (lower-case hex) of the rule version, the sorted run keys and every label in key order.</param>
/// <param name="RuleVersion">The <see cref="TripleBarrier.RuleVersion"/> that labelled it.</param>
/// <param name="RunKeys">The keys of the source runs, sorted ordinally.</param>
/// <param name="FromDate">Earliest trade date among the labelled trades.</param>
/// <param name="ToDate">Latest trade date among the labelled trades.</param>
/// <param name="Positives">Labels equal to 1.</param>
/// <param name="Negatives">Labels equal to 0.</param>
/// <param name="Unresolved">Trades left out because the bars ended while the position was open.</param>
/// <param name="NoTarget">Trades left out because they have no target level.</param>
/// <param name="Labels">The labels, ordered by key (ordinal).</param>
public sealed record TrainingSet(
    string Id,
    string RuleVersion,
    IReadOnlyList<string> RunKeys,
    DateOnly FromDate,
    DateOnly ToDate,
    int Positives,
    int Negatives,
    int Unresolved,
    int NoTarget,
    IReadOnlyList<TripleBarrierLabel> Labels);
