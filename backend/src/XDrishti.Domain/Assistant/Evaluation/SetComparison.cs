namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>The paired comparison of one set between a run and a baseline (spec P9-09 AC 12): counts, discordant pairs, the sign-test p and whether it is a regression. A comparison never blocks by itself: the gate is the thresholds.</summary>
public sealed record SetComparison(string Set, int Cases, int PassesNow, int PassesBefore, PairedResult Paired, bool Regression, string Sentence);
