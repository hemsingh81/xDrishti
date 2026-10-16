namespace XDrishti.Indicators;

/// <summary>The values of the session context (ADR 0031): the previous session that had bars, today so far, and the opening gap.</summary>
public enum SessionField
{
    PrevOpen,
    PrevHigh,
    PrevLow,
    PrevClose,
    PrevVolume,
    TodayOpen,
    TodayHigh,
    TodayLow,
    GapPct,
}
