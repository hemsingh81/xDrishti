namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>The summary of one set: its outcome, its rates with intervals, and the repeat figures (<c>pass@1</c> and <c>stability</c> where they apply).</summary>
public sealed record SetSummary(string Set, SetOutcome Outcome, IReadOnlyList<RateLine> Rates, int Cases, int Repeats, double? Stability, double? IntentMean, double? IntentLowest);
