namespace XDrishti.Domain.Readiness;

/// <summary>
/// Everything the 15-row checklist is judged on (spec CF-11 D1). A null number or boolean, and a <see cref="RowStatus.Unavailable"/> judgement, is an input that does not exist: the row reads unavailable, which is never a pass. The producers that judge their own thresholds
/// (the cost stress, the nulls, the plateau, the hold-out) hand over their word as a <see cref="RowStatus"/>; the rest are figures the checklist judges itself. Rows 2, 3 and 6 are on the development out-of-sample set ("pre-hold-out", review fix 1 of the spec).
/// </summary>
public sealed record ChecklistInputs
{
    /// <summary>Row 1: the rationale and falsifier are registered and the plan hash is frozen.</summary>
    public bool? Registered { get; init; }

    /// <summary>Row 2: the effective number of independent out-of-sample trades.</summary>
    public double? NEff { get; init; }

    /// <summary>Row 2: the fewest out-of-sample trades in any used cell.</summary>
    public int? MinTradesPerCell { get; init; }

    /// <summary>Rows 3 and 15: the net expectancy per trade at 1x costs, in R, out of sample.</summary>
    public double? ExpectancyR { get; init; }

    /// <summary>Rows 3 and 6: the multiple-testing factor <c>Factor(N)</c> of the strategy's trial count.</summary>
    public double? Factor { get; init; }

    /// <summary>Row 6: the standard deviation of the out-of-sample R per trade.</summary>
    public double? SdR { get; init; }

    /// <summary>Row 4: the cost stress at 1.5x (CF-05's own judgement).</summary>
    public RowStatus CostStress15 { get; init; }

    /// <summary>Row 4: the cost stress at 2x (CF-05's own judgement).</summary>
    public RowStatus CostStress2 { get; init; }

    /// <summary>Row 5: the fill one minute later keeps its share of the expectancy (CF-05's own judgement).</summary>
    public RowStatus FillDelay { get; init; }

    /// <summary>Row 7: the random-entry null (CF-07's own judgement).</summary>
    public RowStatus RandomEntry { get; init; }

    /// <summary>Row 8: the placebo set (CF-07's own judgement).</summary>
    public RowStatus Placebo { get; init; }

    /// <summary>Row 8: the mirror (CF-07's own judgement).</summary>
    public RowStatus Mirror { get; init; }

    /// <summary>Row 9: the plateau (CF-07's own judgement).</summary>
    public RowStatus Plateau { get; init; }

    /// <summary>Row 10: the share of calendar quarters at or above the quarter floor.</summary>
    public double? QuarterShare { get; init; }

    /// <summary>Row 10: the largest share of the total R that one year holds.</summary>
    public double? MaxYearShare { get; init; }

    /// <summary>Row 10: the largest share of the total R that one instrument holds.</summary>
    public double? MaxInstrumentShare { get; init; }

    /// <summary>Row 11: the observed maximum drawdown, in R.</summary>
    public double? ObservedMaxDrawdownR { get; init; }

    /// <summary>Row 11: the 90th percentile of the Monte Carlo maximum drawdown, in R.</summary>
    public double? McDrawdownP90 { get; init; }

    /// <summary>Row 11: the 95th percentile of the Monte Carlo maximum drawdown, in R.</summary>
    public double? McDrawdownP95 { get; init; }

    /// <summary>Row 12: the hold-out was used once and passed (CF-06's own judgement).</summary>
    public RowStatus HoldOut { get; init; }

    /// <summary>Row 13: the strategy beats the same-universe benchmark net, risk-adjusted.</summary>
    public bool? BenchmarkBeaten { get; init; }

    /// <summary>Row 14: every trade is tier A or B and no order exceeds 1 % of the minute value.</summary>
    public bool? CapacityOk { get; init; }

    /// <summary>Row 15: the owner's 20-trade check (P3-09) passed.</summary>
    public bool? TwentyTradeCheckPassed { get; init; }

    /// <summary>Row 15: the P10 paper phase check reads MET.</summary>
    public bool? PhaseCheckMet { get; init; }

    /// <summary>Row 15: the forward edge-series mean net R.</summary>
    public double? ForwardMeanR { get; init; }

    /// <summary>Row 15: the standard error of that mean, <c>sd / sqrt(n_eff)</c>.</summary>
    public double? ForwardStandardError { get; init; }

    /// <summary>For each row number, why its input is missing or a short note from the producer; shown beside the row. Never read as a judgement.</summary>
    public IReadOnlyDictionary<int, string> Notes { get; init; } = new Dictionary<int, string>();
}
