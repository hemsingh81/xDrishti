namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>What the plans set (B) counted over its cases (spec P9-09 AC 5).</summary>
public sealed record PlanCounts(int Cases, int RawPass, int RepairedPass, int Escapes, int Covered);
