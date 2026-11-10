namespace XDrishti.Application.Market.Baskets;

/// <summary>One version of a derived basket in the list (<c>outcome</c>: applied, not_formed; <c>provenance</c>: derived or recomputed).</summary>
public sealed record BasketVersionSummary(Guid VersionId, DateOnly AsOf, DateOnly EffectiveFrom, DateTimeOffset? FormedAt, int MemberCount, int UniverseSize, string ParamsHash, string Outcome, string Provenance);

public sealed record BasketVersionPage(IReadOnlyList<BasketVersionSummary> Items, int Total);

/// <summary>A ranked instrument of a version: members and the next non-members by rank (the "why").</summary>
public sealed record VersionScoreView(Guid InstrumentId, string Symbol, string Name, double Score, int Rank, bool IsMember);

public sealed record VersionExclusionView(Guid InstrumentId, string Symbol, string Reason);

/// <summary>One version with its scores, named exclusions (<c>universeSize = members + belowThreshold + excluded</c>) and the parameters it was computed with.</summary>
public sealed record BasketVersionDetail(
    BasketVersionSummary Version,
    int BelowThreshold,
    IReadOnlyList<VersionScoreView> Scores,
    IReadOnlyList<VersionExclusionView> Excluded,
    IReadOnlyDictionary<string, double> Params);

/// <summary>What one basket did in a run: <c>applied</c>, <c>unchanged</c> or <c>skipped</c> (with a reason).</summary>
public sealed record DerivedBasketOutcome(Guid BasketId, string Name, string Outcome, string? Reason, int Members, int Universe, int Excluded);

/// <summary>The result of a refresh or backfill run.</summary>
public sealed record DerivedRun(DateOnly AsOf, IReadOnlyList<DerivedBasketOutcome> Baskets, string Summary, string? Note = null);

/// <summary>The switch and parameters of a derived basket after an owner act.</summary>
public sealed record DerivedDefinitionView(Guid BasketId, string Name, int DefinitionNo, bool Enabled, string ParamsHash, IReadOnlyDictionary<string, double> Overrides);

/// <summary>The body of a backfill request.</summary>
public sealed record DerivedBackfillInput(Guid BasketId, DateOnly? From, DateOnly? To);

/// <summary>The body of a parameter or switch change: new numbers (their overrides of the defaults, a whole new set) and/or the switch.</summary>
public sealed record DerivedParamsInput(IReadOnlyDictionary<string, double>? Overrides, bool? Enabled);
