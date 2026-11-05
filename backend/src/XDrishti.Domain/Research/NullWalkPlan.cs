namespace XDrishti.Domain.Research;

/// <summary>What the random-entry walk of CF-07 is asked to do: the real trades to restate, how many seeds, how many redraws and the seed. All of it is known before any bar is read.</summary>
/// <param name="Trades">The real trades, in plan order.</param>
/// <param name="Draws">Seeds, 1 or more; one seed is one random trade for each real trade.</param>
/// <param name="Redraws">Times a draw that makes no trade is drawn again before the trade is dropped.</param>
/// <param name="Seed">The seed of the run (first 8 bytes of the sha256 of the run key and <c>:random-entry</c>).</param>
public sealed record NullWalkPlan(IReadOnlyList<NullTradeSpec> Trades, int Draws, int Redraws, ulong Seed);
