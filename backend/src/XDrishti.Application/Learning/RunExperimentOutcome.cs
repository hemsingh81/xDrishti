namespace XDrishti.Application.Learning;

/// <summary>What an experiment did.</summary>
/// <param name="Id">The ledger id.</param>
/// <param name="AlreadyRecorded">The same experiment was in the ledger; it was not judged again and nothing was written.</param>
/// <param name="Outcome">Better, NotBetter, Inconclusive or CheaperAssumption.</param>
/// <param name="Trials">The trials it was judged with.</param>
/// <param name="Reasons">Why it is not better; empty when better.</param>
/// <param name="ReportText">The report with its honest limits and the timing note.</param>
/// <param name="CompareMs">Milliseconds spent reading both runs and judging them.</param>
public sealed record RunExperimentOutcome(long Id, bool AlreadyRecorded, string Outcome, int Trials, IReadOnlyList<string> Reasons, string ReportText, long CompareMs);
