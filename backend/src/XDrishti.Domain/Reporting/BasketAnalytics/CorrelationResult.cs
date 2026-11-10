namespace XDrishti.Domain.Reporting;

/// <summary>Average pairwise correlation and the effective number of bets of a basket (BK-06 m11, m12).</summary>
/// <param name="AveragePairwise">The mean Pearson correlation over all pairs with enough common sessions.</param>
/// <param name="EffectiveBets">1 + (N - 1)(1 - rho_bar), clamped to 1..N; a heuristic, not a measured count.</param>
/// <param name="Members">Members the figure rests on (after any sampling).</param>
/// <param name="Pairs">Pairs with enough common sessions.</param>
/// <param name="Sampled">True when the basket had more members than the maximum and the most liquid were used.</param>
public sealed record CorrelationResult(Metric AveragePairwise, Metric EffectiveBets, int Members, int Pairs, bool Sampled);
