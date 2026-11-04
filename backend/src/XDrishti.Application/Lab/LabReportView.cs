namespace XDrishti.Application.Lab;

/// <summary>What was tested: the strategy version, the method, the window and the universe (spec P5-06).</summary>
public sealed record ReportRunView(string Key, string StrategyId, int Version, string Hash, string Method, DateOnly From, DateOnly To, string Universe, int Instruments, IReadOnlyList<string> SimRunKeys);

/// <summary>The headline figures of the out-of-sample evidence; a figure that is not finite is null.</summary>
public sealed record ReportKpisView(int OosTrades, double? ExpectancyR, double? ProfitFactor, double? MaxDrawdownR, int Folds, int PositiveFolds);

/// <summary>A caution derived from the report's own numbers. It never changes the outcome.</summary>
public sealed record ReportWarningView(string Code, string Text);

/// <summary>
/// The report of a finished full or robust test (spec P5-06, doc 11 §4), built from the stored result only. <c>NotIncluded</c> names the sections of doc 11 §4 that the stored result cannot fill, so nothing is invented. <c>DeflatedSharpe</c> (CF-03) is the probability beside the factor, null when not computed. <c>CostStress</c> (CF-05) is the cost stress and the delay test, null when not computed. <c>Trials</c>, <c>Registration</c> and <c>HoldOut</c> (CF-06) are the count of looks with its parts, the pre-registration state and the hold-out state; null in a report built without them. <c>Summary</c> is a deterministic
/// template over the numbers above, not an assistant text.
/// </summary>
public sealed record LabReportView(
    Guid RunId,
    string Outcome,
    int VariantCount,
    double AdjustmentFactor,
    ReportRunView Run,
    ReportKpisView Kpis,
    IReadOnlyList<GateView> Gates,
    IReadOnlyList<CellView> BestCells,
    IReadOnlyList<FullPeriodView> Exploration,
    RobustnessView? Robustness,
    IReadOnlyList<ReportWarningView> Warnings,
    IReadOnlyList<string> HonestLimits,
    IReadOnlyList<string> NotIncluded,
    IReadOnlyList<string> Labels,
    string Summary,
    DeflatedSharpeView? DeflatedSharpe = null,
    CostStressView? CostStress = null,
    TrialsView? Trials = null,
    ReportRegistrationView? Registration = null,
    ReportHoldOutView? HoldOut = null);
