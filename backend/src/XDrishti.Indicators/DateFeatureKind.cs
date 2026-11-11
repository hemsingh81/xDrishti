namespace XDrishti.Indicators;

/// <summary>
/// Which date-driven feature a <see cref="DateFeature"/> reports (P3-01c, ADR 0033): the two calendar features, which need no data but the date of the step, and the ten that read the daily
/// <see cref="MarketContext"/>. Each corresponds to one catalog name (<c>day_of_week</c>, <c>is_expiry_day</c>, <c>index_trend</c>, <c>index_adx</c>, <c>index_vol_pct</c>, <c>vix</c>, <c>vix_pct</c>,
/// <c>vix_chg</c>, <c>breadth</c>, <c>regime_trend</c>, <c>regime_vol</c>, <c>regime</c>).
/// </summary>
public enum DateFeatureKind
{
    /// <summary>The ISO weekday of the India date of the step: Monday 1 … Sunday 7.</summary>
    Weekday,

    /// <summary>1 when the India date of the step is one of the expiry dates the caller gave, else 0.</summary>
    ExpiryDay,

    /// <summary><see cref="ContextValues.IndexTrend"/>.</summary>
    IndexTrend,

    /// <summary><see cref="ContextValues.IndexAdx"/>.</summary>
    IndexAdx,

    /// <summary><see cref="ContextValues.IndexVolPct"/>.</summary>
    IndexVolPct,

    /// <summary><see cref="ContextValues.Vix"/>.</summary>
    Vix,

    /// <summary><see cref="ContextValues.VixPct"/>.</summary>
    VixPct,

    /// <summary><see cref="ContextValues.VixChg"/>.</summary>
    VixChg,

    /// <summary><see cref="ContextValues.Breadth"/>.</summary>
    Breadth,

    /// <summary><see cref="ContextValues.RegimeTrend"/>.</summary>
    RegimeTrend,

    /// <summary><see cref="ContextValues.RegimeVol"/>.</summary>
    RegimeVol,

    /// <summary><see cref="ContextValues.Regime"/>.</summary>
    Regime,

    /// <summary>Sessions to the next results event of the instrument as known at the start of the India date of the step (P15-05); NaN when unknown. Read from <see cref="FrameOptions.ResultsDays"/>.</summary>
    DaysToResults,

    /// <summary>Sessions since the reaction session of the newest results filing visible at the start of the India date of the step (P15-05); NaN when unknown. Read from <see cref="FrameOptions.ResultsDays"/>.</summary>
    DaysSinceResults,

    /// <summary>Quintile 1 to 5 of the <c>sue_yoy</c> of the newest results filing visible at the start of the India date of the step, by the breakpoints of the previous complete season, as of the <b>close</b> of that date (P15-09); NaN when unknown. Read from <see cref="FrameOptions.ResultsDays"/>.</summary>
    ResultsSueQuintile,

    /// <summary>Quintile 1 to 5 of the <c>results_car3</c> of the newest results filing visible at the start of the India date of the step, by the breakpoints of the previous complete season, as of the <b>close</b> of that date (P15-09); NaN when unknown. Read from <see cref="FrameOptions.ResultsDays"/>.</summary>
    ResultsCar3Quintile,

    /// <summary>Volatility-adjusted momentum percent rank (0 to 1) of the instrument inside a pre-registered basket, as of the previous closed session (BK-09); NaN when the stored state is not a value. Read from <see cref="FrameOptions.RelativeStrength"/>.</summary>
    MomRank,

    /// <summary>The instrument's 20-session return minus the Nifty 50's, in percentage points, as of the previous closed session (BK-09); NaN when unavailable. Read from <see cref="FrameOptions.RelativeStrength"/>.</summary>
    RsVsIndex,

    /// <summary>The rank (1 is the strongest) of the instrument's sector by its equal-weight member return over the window, as of the previous closed session (BK-09); NaN when the sector has no rank. Read from <see cref="FrameOptions.RelativeStrength"/>.</summary>
    SectorRs,
}
