namespace XDrishti.Domain.Options.Remark;

/// <summary>The choice of a series: the plan, or the fate (<see cref="RemarkFates"/>) that says why there is none. Exactly one is set.</summary>
public sealed record SeriesPlanResult(SeriesPlan? Plan, string? Fate);
