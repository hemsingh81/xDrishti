namespace XDrishti.Domain.Baskets.Derived;

/// <summary>A member and how many sessions it has been in (0 when just added); the next evaluation counts one more.</summary>
public sealed record DerivedMember(string Symbol, int SessionsHeld);

/// <summary>The score of an evaluated instrument. <c>Rank</c> is 1-based among those passing the gate (best first), 0 when the gate is failed.</summary>
public sealed record DerivedScore(string Symbol, double Score, int Rank);

/// <summary>An instrument left out, with the named reason.</summary>
public sealed record DerivedExclusion(string Symbol, string Reason);

/// <summary>The cluster of an instrument (basket 14): a number from 1, and the majority industry name.</summary>
public sealed record ClusterAssignment(string Symbol, int Cluster, string Name);

/// <summary>
/// The outcome for one date. Members + <c>BelowThreshold</c> + Excluded = the universe: no instrument is dropped silently.
/// <c>Stability</c> is the adjusted Rand index against the previous clustering (basket 14 only).
/// </summary>
public sealed record DerivedResult(
    IReadOnlyList<DerivedMember> Members,
    IReadOnlyList<DerivedScore> Scores,
    IReadOnlyList<DerivedExclusion> Excluded,
    IReadOnlyList<string> BelowThreshold,
    IReadOnlyList<ClusterAssignment> Clusters,
    double? Stability,
    string ParamsHash);
