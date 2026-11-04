using System.Globalization;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Options.Evaluation;

/// <summary>
/// The pre-registered settings of protocol O (P14-09, spec "Protocol O as built"). A different value is a different protocol; <see cref="Canonical"/> is what the records hash. Every number here is the spec's own; the Lab gates are the Lab's defaults
/// (<see cref="QualificationGates.Default"/>), none is invented. The calibration gate does not apply (no probability) and is given 0.
/// </summary>
public sealed record ExpressionStudyOptions
{
    public static ExpressionStudyOptions Default { get; } = new();

    /// <summary>
    /// The settings of protocol R (the condor, P14-10): the spec's own 6 quarters and 200 condors; the fewest condors in a quarter for it to be evaluable is 10, not protocol O's 20, because a weekly entry in a range regime is at most 13 a quarter (OPEN owner question 3 of the ADR: the spec names no per-quarter minimum).
    /// Everything else is protocol O's.
    /// </summary>
    public static ExpressionStudyOptions Condor { get; } = new() { Name = "protocol-r/1", MinPairedPerFold = 10, MinPaired = 200 };

    /// <summary>The first token of <see cref="Canonical"/>: a different protocol is a different hash.</summary>
    public string Name { get; init; } = "protocol-o/1";

    /// <summary>The fewest paired trades in a calendar quarter for it to be an evaluable fold.</summary>
    public int MinPairedPerFold { get; init; } = 20;

    /// <summary>The fewest evaluable folds.</summary>
    public int MinFolds { get; init; } = 6;

    /// <summary>The fewest paired OOS trades, pooled over the evaluable folds.</summary>
    public int MinPaired { get; init; } = 400;

    /// <summary>The smallest mean difference worth having, in R, widened by the factor of N.</summary>
    public double MarginR { get; init; } = 0.05;

    /// <summary>The share of the evaluable folds in which the mean difference must be above 0.</summary>
    public double PositiveFoldShare { get; init; } = 2.0 / 3.0;

    /// <summary>The fewest paired trades in the latest rule epoch for its mean to count (fewer is unavailable).</summary>
    public int MinLatestEpochPaired { get; init; } = 30;

    /// <summary>The largest share of paired OOS trades that used the Black-76 fallback price.</summary>
    public double MaxFallbackShare { get; init; } = 0.05;

    /// <summary>An unpaired share above this is printed as a limit.</summary>
    public double UnpairedLimitShare { get; init; } = 0.30;

    public int Draws { get; init; } = 10_000;

    public ulong Seed { get; init; } = 1337;

    public double Level { get; init; } = 0.90;

    /// <summary>The longer block of the sensitivity, in sessions.</summary>
    public int BlockSessions { get; init; } = 5;

    public int HoldOutMonths { get; init; } = 6;

    public int MinHoldOutPaired { get; init; } = 100;

    public int EmbargoSessions { get; init; } = 5;

    /// <summary>The multiple of the standard error that is the minimum detectable effect (the spec's power note: SD 1.5 R and n 400 give SE 0.075 R and an MDE of about 0.19 R).</summary>
    public double MdeMultiple { get; init; } = 2.5;

    /// <summary>The gates the Lab's verdict function judges the expression's own OOS evidence on.</summary>
    public QualificationGates Gates { get; init; } = QualificationGates.Default;

    /// <summary>True when every setting can be used.</summary>
    public bool IsValid =>
        MinPairedPerFold >= 1 && MinFolds >= 1 && MinPaired >= 1 && double.IsFinite(MarginR) && MarginR > 0 && PositiveFoldShare is > 0 and <= 1 && MinLatestEpochPaired >= 1
        && MaxFallbackShare is >= 0 and <= 1 && UnpairedLimitShare is > 0 and <= 1 && Draws >= 100 && Level is > 0.5 and < 1 && BlockSessions >= 2 && HoldOutMonths is >= 1 and <= 60 && MinHoldOutPaired >= 1 && EmbargoSessions >= 0 && MdeMultiple > 0;

    /// <summary>The stable text of the settings (invariant culture, fixed order): the options hash of the records.</summary>
    public string Canonical()
    {
        var c = CultureInfo.InvariantCulture;
        return string.Create(
            c,
            $"{Name};minFold={MinPairedPerFold};folds={MinFolds};paired={MinPaired};margin={MarginR:R};pos={PositiveFoldShare:R};epochN={MinLatestEpochPaired};fallback={MaxFallbackShare:R};unpaired={UnpairedLimitShare:R};draws={Draws};seed={Seed};level={Level:R};block={BlockSessions};holdout={HoldOutMonths}/{MinHoldOutPaired}/{EmbargoSessions};mde={MdeMultiple:R};gates={Gates.MinOosTrades}/{Gates.MinExpectancyR:R}/{Gates.MinProfitFactor:R}/{Gates.MinPositiveFoldsPct:R}/{Gates.MaxDrawdownR:R}/{Gates.MaxCalibrationErrorPp:R}");
    }
}
