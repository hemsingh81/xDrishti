namespace XDrishti.Indicators;

/// <summary>
/// The derived values of one <see cref="MarketContext"/> day (ADR 0033), each computed from that day and the days before it only. A value that is not ready — or whose input the day does not have — is
/// <see cref="double.NaN"/>; the DSL treats it as a false condition. <see cref="Date"/> is the day the values belong to (for <see cref="MarketContext.TryAsOf(DateOnly, out ContextValues)"/>: the day before the
/// date asked for). The names are the context features: <see cref="IndexTrend"/> is +1 (up), 0 (range) or −1 (down); <see cref="IndexAdx"/> the Wilder ADX of the index; <see cref="IndexVolPct"/> the percentile
/// rank (0 to 100) of the index's ATR as a percent of its close; <see cref="Vix"/>, <see cref="VixPct"/> and <see cref="VixChg"/> the VIX close, its percentile rank and its percent change against the previous
/// day of the context; <see cref="Breadth"/> the value the day carried; <see cref="RegimeTrend"/> (the same as <see cref="IndexTrend"/>), <see cref="RegimeVol"/> (0 low, 1 normal, 2 high) and
/// <see cref="Regime"/> (1 to 9, see <see cref="XDrishti.Indicators.Regime"/>).
/// </summary>
public readonly record struct ContextValues(
    DateOnly Date,
    double IndexTrend,
    double IndexAdx,
    double IndexVolPct,
    double Vix,
    double VixPct,
    double VixChg,
    double Breadth,
    double RegimeTrend,
    double RegimeVol,
    double Regime);
