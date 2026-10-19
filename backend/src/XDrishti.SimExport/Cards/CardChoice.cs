using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Cards;

/// <summary>A trade that gets a card: its place in the run's trade order (1-based), the trade and the name of the stratum it was chosen from (<c>stop-buy</c>; <c>other</c> for a value outside the known set).</summary>
public sealed record CardChoice(int Number, RunTrade Trade, string Stratum);
