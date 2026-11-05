namespace XDrishti.Domain.Learning;

/// <summary>One completed non-overlapping window of the decay monitor (spec CF-12, D1).</summary>
/// <param name="Index">The 1-based number of the window.</param>
/// <param name="Trades">Trades in it (the window size).</param>
/// <param name="FirstDay">The day of its first trade.</param>
/// <param name="LastDay">The day of its last trade.</param>
/// <param name="MeanR">The mean R of the window.</param>
/// <param name="DesignEffect">The mean design effect of the days of its trades.</param>
/// <param name="Threshold">The alert level for this window, <c>mu_ref - z * sigma * sqrt(DEFF / n + 1 / N_ref)</c>.</param>
/// <param name="Below">True when the mean is strictly below the threshold.</param>
public sealed record DecayWindow(int Index, int Trades, DateOnly FirstDay, DateOnly LastDay, double MeanR, double DesignEffect, double Threshold, bool Below);
