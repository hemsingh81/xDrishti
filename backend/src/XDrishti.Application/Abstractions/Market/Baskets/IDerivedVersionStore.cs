using XDrishti.Application.Market.Baskets;

namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>A derived basket's switch and parameters: <c>Overrides</c> are those of the selected parameter set (none = the defaults).</summary>
public sealed record DerivedDefinitionRow(Guid BasketId, string BasketName, int DefinitionNo, bool Enabled, Guid? ParamSetId, IReadOnlyDictionary<string, double> Overrides);

/// <summary>The score of one ranked instrument of a version; <c>SessionsHeld</c> is what the engine counted for a member (0 when just added).</summary>
public sealed record VersionScore(Guid InstrumentId, double Score, int Rank, bool IsMember, int SessionsHeld);

/// <summary>An instrument left out of a version, with the named reason.</summary>
public sealed record VersionExclusion(Guid InstrumentId, string Symbol, string Reason);

/// <summary>What is stored for one version of a derived basket (BK-03). <c>ValidTo</c> is set only for a backfilled version, whose stays are closed when written.</summary>
public sealed record DerivedVersionWrite(
    Guid VersionId,
    Guid BasketId,
    string BasketName,
    DateOnly AsOf,
    DateOnly EffectiveFrom,
    DateOnly? ValidTo,
    DateTimeOffset? FormedAt,
    string ParamsJson,
    string ParamsHash,
    int UniverseSize,
    int BelowThreshold,
    int ExcludedCount,
    string ExcludedJson,
    string Provenance,
    string Outcome,
    IReadOnlyList<VersionScore> Scores,
    DateTimeOffset CreatedAt,
    string Actor);

/// <summary>The newest published version of a basket, before a new one is formed.</summary>
public sealed record PreviousVersion(Guid VersionId, DateOnly AsOf, DateOnly EffectiveFrom, IReadOnlyList<VersionScore> Scores);

/// <summary>What a write did: <c>applied</c> (with the counts of changes), <c>unchanged</c> (the same as_of and parameters exist) or <c>behind</c> (a newer published version exists, so nothing was written).</summary>
public sealed record DerivedWriteResult(string Outcome, int Added, int Removed)
{
    public const string Behind = "behind";
}

/// <summary>A parameter set to store: the overrides of the defaults and the hash of the resulting numbers.</summary>
public sealed record NewParamSet(Guid Id, int DefinitionNo, string ParamsJson, string ParamsHash, DateTimeOffset CreatedAt);

/// <summary>Persistence of the derived baskets: definitions, versions, scores and the dated members. Reads are not tracked; <see cref="WriteAsync"/> is one transaction per version.</summary>
public interface IDerivedVersionStore
{
    ValueTask<IReadOnlyList<DerivedDefinitionRow>> ListDefinitionsAsync(CancellationToken cancellationToken);

    /// <summary>The newest published (<c>derived</c>) version with an as_of before <paramref name="asOf"/>, with its scores; null when there is none.</summary>
    ValueTask<PreviousVersion?> PreviousAsync(Guid basketId, DateOnly asOf, CancellationToken cancellationToken, string provenance = "derived");

    /// <summary>Whether a <c>recomputed</c> version of the basket with an as_of from <paramref name="from"/> to <paramref name="to"/> exists under a different parameter hash (a second series would overlap the first).</summary>
    ValueTask<bool> HasOtherRecomputedAsync(Guid basketId, DateOnly from, DateOnly to, string paramsHash, CancellationToken cancellationToken);

    /// <summary>The effective date of the first and the as_of of the newest published version of the basket (both null when there is none).</summary>
    ValueTask<(DateOnly? FirstEffectiveFrom, DateOnly? NewestAsOf)> PublishedRangeAsync(Guid basketId, CancellationToken cancellationToken);

    ValueTask<bool> ExistsAsync(Guid basketId, DateOnly asOf, string paramsHash, CancellationToken cancellationToken);

    /// <summary>Writes the version, its scores and the dated member changes in one transaction (nothing is left behind on a failure).</summary>
    ValueTask<DerivedWriteResult> WriteAsync(DerivedVersionWrite version, CancellationToken cancellationToken);

    ValueTask<BasketVersionPage?> ListVersionsAsync(Guid basketId, int skip, int take, CancellationToken cancellationToken);

    ValueTask<BasketVersionDetail?> GetVersionAsync(Guid basketId, Guid versionId, CancellationToken cancellationToken);

    /// <summary>Stores a new immutable parameter set and selects it (when <paramref name="paramSet"/> is given), switches the basket (when <paramref name="enabled"/> is given), with the audit event; false when the basket has no definition.</summary>
    ValueTask<bool> SaveDefinitionAsync(Guid basketId, bool? enabled, NewParamSet? paramSet, string auditText, string actor, CancellationToken cancellationToken);
}
