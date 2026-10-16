namespace XDrishti.Indicators;

/// <summary>
/// Which date-driven feature a <see cref="ContextFeature"/> reports (P3-01c, ADR 0033): the two calendar features, which need no data but the date of the step, and the ten that read the daily
/// <see cref="MarketContext"/>. Each corresponds to one catalog name (<c>day_of_week</c>, <c>is_expiry_day</c>, <c>index_trend</c>, <c>index_adx</c>, <c>index_vol_pct</c>, <c>vix</c>, <c>vix_pct</c>,
/// <c>vix_chg</c>, <c>breadth</c>, <c>regime_trend</c>, <c>regime_vol</c>, <c>regime</c>).
/// </summary>
public enum ContextFeatureKind
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
}
