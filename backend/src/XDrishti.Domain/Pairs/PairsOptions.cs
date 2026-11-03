namespace XDrishti.Domain.Pairs;

/// <summary>
/// The pre-registered settings of the pairs core (spec P14-12). A different value is a different trial (protocol S). Defaults: <c>Lags</c> 1, <c>MinObservations</c> 60, <c>MinWindow</c> 30, <c>Q</c> 0.05, <c>MinCorrelation</c> 0.8,
/// half-life 1 to 20 sessions, entry 2.0, exit 0.5, stop 3.5.
/// </summary>
public sealed record PairsOptions
{
    public static PairsOptions Default { get; } = new();

    /// <summary>Lagged differences in the Engle-Granger regression (0 or more).</summary>
    public int Lags { get; init; } = 1;

    public int MinObservations { get; init; } = 60;

    public int MinWindow { get; init; } = 30;

    /// <summary>The Benjamini-Hochberg false-discovery level.</summary>
    public double Q { get; init; } = 0.05;

    /// <summary>The least return correlation of a candidate pair (used by the candidate rule of P14-13; the core carries it so it is one pre-registered object).</summary>
    public double MinCorrelation { get; init; } = 0.8;

    public double HalfLifeMinSessions { get; init; } = 1.0;

    public double HalfLifeMaxSessions { get; init; } = 20.0;

    public double Entry { get; init; } = 2.0;

    public double Exit { get; init; } = 0.5;

    public double Stop { get; init; } = 3.5;

    /// <summary>True when every setting can be used: lags at least 0, windows at least 3, thresholds finite with 0 &lt;= exit &lt; entry &lt; stop, q in (0, 1].</summary>
    public bool IsValid =>
        Lags >= 0 && Lags <= 20 && MinObservations >= 5 && MinWindow >= 3
        && double.IsFinite(Q) && Q > 0 && Q <= 1
        && double.IsFinite(MinCorrelation) && MinCorrelation >= -1 && MinCorrelation <= 1
        && double.IsFinite(HalfLifeMinSessions) && double.IsFinite(HalfLifeMaxSessions) && HalfLifeMinSessions > 0 && HalfLifeMaxSessions >= HalfLifeMinSessions
        && double.IsFinite(Entry) && double.IsFinite(Exit) && double.IsFinite(Stop) && Exit >= 0 && Exit < Entry && Entry < Stop;
}
