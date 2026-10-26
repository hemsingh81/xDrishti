namespace XDrishti.Domain.Paper;

/// <summary>How many resamples the backtest band takes (spec P10-04, criterion 5).</summary>
/// <param name="Draws">Resamples, <see cref="MinDraws"/> to <see cref="MaxDraws"/>.</param>
public sealed record BandOptions(int Draws = BandOptions.DefaultDraws)
{
    /// <summary>The fewest resamples.</summary>
    public const int MinDraws = 1_000;

    /// <summary>The most resamples.</summary>
    public const int MaxDraws = 100_000;

    /// <summary>The default.</summary>
    public const int DefaultDraws = 10_000;

    /// <summary>Whether <see cref="Draws"/> is in range.</summary>
    public bool IsValid => Draws is >= MinDraws and <= MaxDraws;
}
