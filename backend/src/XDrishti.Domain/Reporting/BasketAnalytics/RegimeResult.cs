namespace XDrishti.Domain.Reporting;

/// <summary>The basket index by regime (BK-06 m16).</summary>
/// <param name="Status">The number of groups when any exists; otherwise why not (unavailable when no regime is recorded).</param>
/// <param name="Rows">One per regime code seen, in code order; thin ones are insufficient rows, not dropped.</param>
/// <param name="Unknown">Days whose previous session has no regime code, in no group.</param>
public sealed record RegimeResult(Metric Status, IReadOnlyList<RegimeRow> Rows, int Unknown);
