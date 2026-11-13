using XDrishti.Domain.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>One stored version of the auto scope (spec AU-16): <c>planning.auto_scopes</c> is insert-only; the newest row is in force.</summary>
/// <param name="Id">The version.</param>
/// <param name="BasketIds">The chosen baskets.</param>
/// <param name="InstrumentIds">The chosen individual instruments.</param>
/// <param name="Hash">The scope hash.</param>
/// <param name="CreatedBy">Who set it.</param>
/// <param name="CreatedAt">When.</param>
public sealed record StoredAutoScope(long Id, IReadOnlyList<Guid> BasketIds, IReadOnlyList<Guid> InstrumentIds, string Hash, string CreatedBy, DateTimeOffset CreatedAt);

/// <summary>Where the auto scope versions are kept.</summary>
public interface IAutoScopeStore
{
    /// <summary>The newest version, or null when none was ever set.</summary>
    ValueTask<StoredAutoScope?> LatestAsync(CancellationToken cancellationToken);

    /// <summary>Appends a version and returns it.</summary>
    ValueTask<StoredAutoScope> AppendAsync(AutoScope scope, string actor, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>The scope as the plan builder reads it: the hash and every instrument id it covers now (members of the baskets plus the individual instruments).</summary>
/// <param name="Hash">The scope hash, stored on the plan.</param>
/// <param name="InstrumentIds">The covered instruments.</param>
/// <param name="BasketsBeforeKnownSince">Baskets whose members were asked for a date before their membership is known (a stored system basket, ADR 0254 item 6): the first known members were used, not proven ones. Empty on a live read.</param>
public sealed record ResolvedAutoScope(string Hash, IReadOnlySet<Guid> InstrumentIds, IReadOnlyList<Guid>? BasketsBeforeKnownSince = null);

/// <summary>The scope to build a plan under, or null when none is set (then every eligible instrument is considered).</summary>
public interface IAutoScopeSource
{
    /// <summary>The scope with its baskets expanded to their members at the close of <paramref name="asOf"/> (the plan session, ADR 0287); null = members now.</summary>
    ValueTask<ResolvedAutoScope?> ResolveAsync(DateOnly? asOf, CancellationToken cancellationToken);
}
