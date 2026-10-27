namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>What the ideas set (C) counted over its cases (spec P9-09 AC 6): <paramref name="Intent"/> is assertions passed over assertions total, <paramref name="LowestIdeaIntent"/> the worst single idea.</summary>
public sealed record IdeaCounts(int Cases, int FirstShotValid, int RepairedValid, double Intent, double LowestIdeaIntent, int DeclineCorrect, int DeclineCases);
