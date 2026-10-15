namespace XDrishti.Indicators;

/// <summary>How an indicator treats the end of a session (ADR 0031 §4).</summary>
public enum SessionBehaviour
{
    /// <summary>It runs on across sessions on the series it is fed: an intraday EMA(20) at 09:16 uses the previous day's bars.</summary>
    Continuous,

    /// <summary>It starts afresh at the first bar of each India trading date.</summary>
    PerSession,
}
