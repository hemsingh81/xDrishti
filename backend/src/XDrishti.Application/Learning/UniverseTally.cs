namespace XDrishti.Application.Learning;

/// <summary>How the liquidity tiers of a set of trades came out (BK-11); every output that uses tiers prints it so a thin history is visible.</summary>
/// <param name="TierCells">The tier cells made (each has three window rows).</param>
/// <param name="Tiered">Trades in a tier cell.</param>
/// <param name="Unknown">Trades with no tier: too few bars, or no dated membership on their decision date. Counted, never given today's tier.</param>
/// <param name="Unavailable">Trades dated when fewer shares than the minimum could be ranked.</param>
public sealed record UniverseTally(int TierCells, int Tiered, int Unknown, int Unavailable);
