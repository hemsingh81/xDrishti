namespace XDrishti.Api.Endpoints;

/// <summary>The body of a day-by-day simulation start (SR-04). Defaults: end the last complete stored session (clamped before hold-out block 1 unless its strategy look is stored), capital <c>Sim:DefaultCapital</c>, the seeded standard profile, mode learning. Costs are gross (ADR 0292).</summary>
internal sealed record SimDaysStartBody(DateOnly Start, DateOnly? End, ReplayScopeBody? Scope, decimal? Capital, Guid? RiskProfileId, string? Mode);

/// <summary>The body of the auto-run switch.</summary>
internal sealed record SimDaysAutoBody(bool On);

/// <summary>The answer of a start.</summary>
internal sealed record SimDaysStarted(Guid Id);
