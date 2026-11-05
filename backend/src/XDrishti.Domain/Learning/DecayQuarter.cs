namespace XDrishti.Domain.Learning;

/// <summary>One calendar quarter of the by-quarter table of the decay monitor (spec CF-12, D1).</summary>
/// <param name="Year">The year.</param>
/// <param name="Quarter">1 to 4.</param>
/// <param name="Trades">Trades whose day falls in the quarter.</param>
/// <param name="MeanR">Their mean R.</param>
/// <param name="Weak">True when the mean is strictly below the quarter floor.</param>
public sealed record DecayQuarter(int Year, int Quarter, int Trades, double MeanR, bool Weak);
