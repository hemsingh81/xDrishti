namespace XDrishti.Indicators;

/// <summary>How an indicator treats the end of a session (ADR 0031 §4).</summary>
public enum SessionBehaviour
{
    /// <summary>It runs on across sessions on the series it is fed: an intraday EMA(20) at 09:16 uses the previous day's bars.</summary>
    Continuous,

    /// <summary>
    /// It starts afresh at the first bar of each India trading date — and so does its value history in the <see cref="FeatureFrame"/> on intraday timeframes: <see cref="FeatureFrame.At"/> reads the current session only,
    /// and the value is unknown from the start of a new date until the feature's timeframe has closed its first bar of it (ADR 0035; see <see cref="IndicatorInfo.RestartsEachSession"/>).
    /// </summary>
    PerSession,
}
