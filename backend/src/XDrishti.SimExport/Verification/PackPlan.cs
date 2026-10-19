namespace XDrishti.SimExport.Verification;

/// <summary>What the pack holds (spec P3-09): the trade rows, the spot lines and, for each category of <see cref="EdgeCategories.Codes"/>, whether a trade of the pooled runs belongs to it (a statement of presence, never a count).</summary>
public sealed record PackPlan(IReadOnlyList<PackRow> Rows, IReadOnlyList<SpotRow> Spots, IReadOnlyDictionary<string, bool> Occurs);
