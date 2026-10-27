namespace XDrishti.Application.Lab;

/// <summary>One run of the sweep (the base has an empty <c>Param</c>): the figures of a plain run of one exit, or the reason the engine refused it. In-sample.</summary>
public sealed record SweepPointView(string Param, double Value, int Trades, double? MeanR, double? ProfitFactor, double? TotalR, string? Error);

/// <summary>The sweep of one parameter and its judgement (<c>stable</c>, <c>fragile</c>, <c>no_base_edge</c>, <c>inconclusive</c>).</summary>
public sealed record ParameterSensitivityView(string Param, IReadOnlyList<SweepPointView> Points, string Verdict, string Reason);

/// <summary>The parameter sensitivity (spec P5-05): the exit and timeframe every point used, the base and the parameters.</summary>
public sealed record SensitivityView(string Timeframe, string Exit, SweepPointView? Base, IReadOnlyList<ParameterSensitivityView> Parameters, string Label);

/// <summary>The bootstrap of the out-of-sample trades; the seed is text (a 64-bit number).</summary>
public sealed record MonteCarloView(int Trades, int Draws, string Seed, double ObservedMeanR, double ObservedMaxDrawdownR, double MeanP05, double MeanP50, double MeanP95, double DrawdownP50, double DrawdownP95, double ShareNotPositive);

/// <summary>The out-of-sample trades of one calendar year.</summary>
public sealed record YearView(int Year, int Trades, double MeanR, double TotalR, double? ProfitFactor, bool Positive);

/// <summary>The sign-flip baseline: lenient (mean R against 0 given the trade sizes), not a re-run with random entries.</summary>
public sealed record SignFlipBaselineView(string Kind, int Trades, int Draws, string Seed, double ObservedMeanR, double NullMeanP95, double PValue, string Label);

/// <summary>One regime part of the out-of-sample trades (P6-02): the regime and its basis are one bucket; below 30 trades it is shown but is not evidence.</summary>
public sealed record RegimeBucketView(string Regime, string Basis, int Trades, double MeanR, double TotalR, bool Enough);

/// <summary>Out-of-sample trades with no regime label, by reason (P6-02).</summary>
public sealed record RegimeMissingView(string Reason, int Trades);

/// <summary>The robustness block of a result (spec P5-05, ADR 0062). It does not change the verdict; every part that cannot be computed is null or says why. <c>ByRegime</c> is the one-line text; since P6-02 <c>ByRegimeBuckets</c> and <c>ByRegimeUnavailable</c> carry the numbers (null in a result stored before it).</summary>
public sealed record RobustnessView(
    SensitivityView Sensitivity,
    MonteCarloView? MonteCarlo,
    IReadOnlyList<YearView> ByYear,
    SignFlipBaselineView? SignFlipBaseline,
    string ByRegime,
    IReadOnlyList<string> Limits,
    IReadOnlyList<RegimeBucketView>? ByRegimeBuckets = null,
    IReadOnlyList<RegimeMissingView>? ByRegimeUnavailable = null);
