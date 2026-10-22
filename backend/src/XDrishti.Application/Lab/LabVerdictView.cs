namespace XDrishti.Application.Lab;

/// <summary>The out-of-sample numbers a verdict was judged on. A number that is not finite (an infinite profit factor) is null; the reason of its gate says it in words.</summary>
public sealed record EvidenceView(int OosTrades, double? ExpectancyR, double? ProfitFactor, int Folds, int PositiveFolds, double? MaxDrawdownR, double CalibrationErrorPp, string Method);

/// <summary>One gate of a verdict as served (spec P5-03, P5-04); <c>Actual</c> is null when it is not finite.</summary>
public sealed record GateView(string Name, double Required, double Adjusted, double? Actual, bool Passed, string Reason);

/// <summary>One cell (timeframe and side) of a full test: the exit the walk-forward chose and how stable that choice was.</summary>
public sealed record CellView(string Cell, string Timeframe, string Side, string Default, string Chosen, string? NoChoiceReason, bool? Stable, string StabilityReason, int Folds, int UsableFolds, int OosTrades, double? OosMeanR, double? DefaultOosMeanR);

/// <summary>A full-period (in-sample) figure of one candidate in one cell. It is exploration and never reaches the verdict.</summary>
public sealed record FullPeriodView(string Cell, string Candidate, int Trades, double? MeanR, double? ProfitFactor, double? TotalR, string Label);

/// <summary>The verdict of a full test with everything it rests on (spec P5-04): outcome, gates, limits, the cells and the exploration block.</summary>
public sealed record LabVerdictView(
    string Outcome,
    int VariantCount,
    double AdjustmentFactor,
    EvidenceView Evidence,
    IReadOnlyList<GateView> Gates,
    IReadOnlyList<string> Limits,
    IReadOnlyList<CellView> Cells,
    IReadOnlyList<FullPeriodView> FullPeriod,
    IReadOnlyList<string> Labels,
    RobustnessView? Robustness = null);
