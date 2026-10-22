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

/// <summary>The sign-flip baseline: not a re-run with random entries.</summary>
public sealed record RandomBaselineView(string Kind, int Trades, int Draws, string Seed, double ObservedMeanR, double NullMeanP95, double PValue, string Label);

/// <summary>The robustness block of a result (spec P5-05, ADR 0062). It does not change the verdict; every part that cannot be computed is null or says why.</summary>
public sealed record RobustnessView(
    SensitivityView Sensitivity,
    MonteCarloView? MonteCarlo,
    IReadOnlyList<YearView> ByYear,
    RandomBaselineView? RandomBaseline,
    string ByRegime,
    IReadOnlyList<string> Limits);
