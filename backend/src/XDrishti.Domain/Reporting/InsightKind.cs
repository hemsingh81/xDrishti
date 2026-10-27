namespace XDrishti.Domain.Reporting;

/// <summary>The auto-insights of doc 15 section 6, each one a rule over the cells of a grouping.</summary>
public enum InsightKind
{
    /// <summary>The strategy with the highest expectancy.</summary>
    BestStrategy,

    /// <summary>The strategy with the lowest expectancy.</summary>
    WeakestStrategy,

    /// <summary>The best and the worst basket.</summary>
    BasketGap,

    /// <summary>Long against short.</summary>
    SideBias,

    /// <summary>The exit with the highest expectancy.</summary>
    BestExit,

    /// <summary>The best and the worst weekday.</summary>
    Weekday,

    /// <summary>The most and least favourable regime: unavailable in the report engine (P6-02: the regime cells and the Lab carry it).</summary>
    Regime,

    /// <summary>Predicted against actual win rate: not evaluable without a champion model.</summary>
    Calibration,
}
