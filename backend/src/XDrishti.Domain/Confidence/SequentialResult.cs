namespace XDrishti.Domain.Confidence;

/// <summary>
/// The result of the sequential test of a series (spec CF-09): the state, the running sum, the boundaries, the first trade at which each level was reached and the sum path. A flag only: no member of it says the strategy is good enough to
/// go on, and nothing reads it as a decision. Pure and deterministic: the same series and settings give the same result.
/// </summary>
/// <param name="State">The state now.</param>
/// <param name="Sum">The running sum of the log likelihood ratios, reflected at the on-track level.</param>
/// <param name="CautionBoundary">The caution boundary (negative).</param>
/// <param name="FailureBoundary">The failure boundary (negative, below the caution boundary).</param>
/// <param name="OnTrackLevel">The level at which the sum is reflected.</param>
/// <param name="Trades">Trades in the series.</param>
/// <param name="EffectiveN">The trades over the design effect of their days.</param>
/// <param name="Rho">The same-day correlation used.</param>
/// <param name="SigmaUsed">The sigma of the last step.</param>
/// <param name="FirstCautionTrade">The 1-based number of the first trade whose sum was at or below the caution boundary; null when never.</param>
/// <param name="FirstFailureTrade">The 1-based number of the first trade whose sum was at or below the failure boundary; null when never.</param>
/// <param name="LowestSum">The lowest sum of the path (0 when there is no trade).</param>
/// <param name="LowestAtTrade">The 1-based trade of the lowest sum; 0 when there is no trade.</param>
/// <param name="Path">The running sum after each trade.</param>
public sealed record SequentialResult(
    SequentialState State,
    double Sum,
    double CautionBoundary,
    double FailureBoundary,
    double OnTrackLevel,
    int Trades,
    double EffectiveN,
    double Rho,
    double SigmaUsed,
    int? FirstCautionTrade,
    int? FirstFailureTrade,
    double LowestSum,
    int LowestAtTrade,
    IReadOnlyList<double> Path);
