namespace XDrishti.Application.Lab;

/// <summary>What was tested: the strategy version, the method, the window and the universe (spec P5-06).</summary>
public sealed record ReportRunView(string Key, string StrategyId, int Version, string Hash, string Method, DateOnly From, DateOnly To, string Universe, int Instruments, IReadOnlyList<string> SimRunKeys);

/// <summary>The headline figures of the out-of-sample evidence; a figure that is not finite is null.</summary>
public sealed record ReportKpisView(int OosTrades, double? ExpectancyR, double? ProfitFactor, double? MaxDrawdownR, int Folds, int PositiveFolds);

/// <summary>A caution derived from the report's own numbers. It never changes the outcome.</summary>
public sealed record ReportWarningView(string Code, string Text);

/// <summary>
/// The report of a finished full or robust test (spec P5-06, doc 11 §4), built from the stored result only. <c>NotIncluded</c> names the sections of doc 11 §4 that the stored result cannot fill, so nothing is invented. <c>Summary</c> is a deterministic
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
    string Summary);
