namespace XDrishti.Application.Reporting;

/// <summary>Trades whose favourable excursion reached the level, and their share of the trades with a recorded excursion.</summary>
/// <param name="AtR">The level in R.</param>
/// <param name="Count">Trades that reached it.</param>
/// <param name="Share">Count over the trades with a recorded excursion.</param>
public sealed record ReachedMfeView(double AtR, int Count, double Share);
