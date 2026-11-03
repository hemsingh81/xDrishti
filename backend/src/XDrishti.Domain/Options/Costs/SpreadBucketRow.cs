namespace XDrishti.Domain.Options.Costs;

/// <summary>One stored bucket of a model: the keys (a merged split is <see cref="SpreadBuckets.All"/>), the quotes the median rests on, and the median half-spread as a percent of the mid.</summary>
public sealed record SpreadBucketRow(string Moneyness, string Dte, string Tod, int N, double HalfSpreadPct);
