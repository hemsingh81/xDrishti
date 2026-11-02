using System.Globalization;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// Every knob of the feature-set comparison (P13-05), printed with the result and part of the trial that the ledger counts. <see cref="ForDepth"/> is the P13 preset (monthly folds, day blocks), <see cref="ForFilings"/> the P15 preset (quarterly folds, season blocks).
/// <see cref="RecipeOptionsHash"/> and <see cref="LabelRuleVersion"/> have no default: the caller names the recipe's options and the label rule, so the trial text is complete.
/// </summary>
public sealed record ExtensionComparisonOptions
{
    /// <summary>The unit of a test fold.</summary>
    public FoldUnit Unit { get; init; } = FoldUnit.Month;

    /// <summary>The first test window starts on the first unit boundary at least this many months after the first row date.</summary>
    public int MinTrainMonths { get; init; } = 2;

    /// <summary>Sessions before a window whose rows are not train rows.</summary>
    public int EmbargoSessions { get; init; } = 3;

    /// <summary>A fold with fewer train rows is skipped.</summary>
    public int MinTrainTrades { get; init; } = 30;

    /// <summary>Rows on or after this date are refused (the hold-out); null when none is reserved.</summary>
    public DateOnly? HoldOutStart { get; init; }

    /// <summary>The take policy (ADR 0075): the kept-share floor, the threshold grid and the scores needed to choose a threshold. Its other knobs are not used here.</summary>
    public MetaPolicyOptions Take { get; init; } = new();

    /// <summary>What a bootstrap block is.</summary>
    public BlockKind Block { get; init; } = BlockKind.Sessions;

    /// <summary>Sessions per block when <see cref="Block"/> is <see cref="BlockKind.Sessions"/>.</summary>
    public int BlockSessions { get; init; } = 1;

    /// <summary>The longer block of the sensitivity run (KEEP needs both bounds); 0 for none.</summary>
    public int SensitivityBlockSessions { get; init; } = 5;

    /// <summary>Bootstrap draws.</summary>
    public int Draws { get; init; } = 10_000;

    /// <summary>The bootstrap seed.</summary>
    public ulong Seed { get; init; } = 1337;

    /// <summary>The one-sided level of the bounds.</summary>
    public double Level { get; init; } = 0.90;

    /// <summary>Fewer evaluable folds than this: not evaluable.</summary>
    public int MinEvaluableFolds { get; init; } = 6;

    /// <summary>Fewer paired candidates than this in the evaluable folds: not evaluable.</summary>
    public int MinPairedCandidates { get; init; } = 2_000;

    /// <summary>KEEP needs at least this many rows where A and B decide differently; 0 for no such rule.</summary>
    public int MinFlips { get; init; } = 300;

    /// <summary>The gain in R per candidate that the lower bound must reach (times the multiple-testing factor) and that the upper bound must stay below for DROP (not widened).</summary>
    public double MinGainR { get; init; } = 0.02;

    /// <summary>The share of the evaluable folds in which the gain must be positive.</summary>
    public double PositiveFoldShare { get; init; } = 2.0 / 3.0;

    /// <summary>The pooled change in log loss must be at most this (negative), times the factor.</summary>
    public double MaxDeltaLogLoss { get; init; } = -0.005;

    /// <summary>The pooled change in Brier score must be at most this (negative), times the factor; null when the rule is not used.</summary>
    public double? MaxDeltaBrier { get; init; } = -0.002;

    /// <summary>B's expected calibration error, in percentage points, must be at most this.</summary>
    public double MaxEcePp { get; init; } = 5;

    /// <summary>The hold-out confirmation is not evaluable under this many rows.</summary>
    public int MinHoldOutRows { get; init; } = 100;

    /// <summary>A hash of the recipe's options (the caller's), part of the trial.</summary>
    public string RecipeOptionsHash { get; init; } = string.Empty;

    /// <summary>The label rule version of the rows, part of the trial.</summary>
    public string LabelRuleVersion { get; init; } = string.Empty;

    /// <summary>The P13 preset: monthly folds, a 2-month first train window, a 3-session embargo, day blocks with a 5-session sensitivity, at least 2,000 paired candidates and 300 flips.</summary>
    public static ExtensionComparisonOptions ForDepth() => new();

    /// <summary>The P15 preset: calendar-quarter folds, a 12-month first train window, a 5-session embargo, season blocks with no sensitivity run, at least 1,000 paired candidates, no flip rule and no Brier rule.</summary>
    public static ExtensionComparisonOptions ForFilings() => new()
    {
        Unit = FoldUnit.Quarter,
        MinTrainMonths = 12,
        EmbargoSessions = 5,
        Block = BlockKind.Season,
        SensitivityBlockSessions = 0,
        MinPairedCandidates = 1_000,
        MinFlips = 0,
        MaxDeltaBrier = null,
    };

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(Unit) || !Enum.IsDefined(Block))
        {
            problems.Add("fold unit and block kind must be known");
        }

        if (MinTrainMonths is < 1 or > 120 || EmbargoSessions is < 0 or > 100 || MinTrainTrades is < 1 or > 1_000_000)
        {
            problems.Add("min_train_months 1 to 120, embargo_sessions 0 to 100, min_train_trades 1 to 1000000");
        }

        problems.AddRange(Take.Problems());
        if (BlockSessions is < 1 or > 100 || SensitivityBlockSessions is < 0 or > 100 || (SensitivityBlockSessions > 0 && SensitivityBlockSessions <= BlockSessions))
        {
            problems.Add("block_sessions 1 to 100 and the sensitivity block 0 or longer than the block, at most 100");
        }

        if (Draws is < 100 or > 1_000_000 || !double.IsFinite(Level) || Level is <= 0.5 or >= 1)
        {
            problems.Add("draws 100 to 1000000 and level above 0.5 and below 1");
        }

        if (MinEvaluableFolds is < 1 or > 1000 || MinPairedCandidates is < 1 or > 100_000_000 || MinFlips is < 0 or > 100_000_000 || MinHoldOutRows is < 1 or > 100_000_000)
        {
            problems.Add("min_evaluable_folds, min_paired_candidates and min_hold_out_rows must be positive and min_flips not negative");
        }

        if (!double.IsFinite(MinGainR) || MinGainR is < 0 or > 10 || !double.IsFinite(PositiveFoldShare) || PositiveFoldShare is <= 0 or > 1)
        {
            problems.Add("min_gain_r 0 to 10 and positive_fold_share above 0 and at most 1");
        }

        if (!double.IsFinite(MaxDeltaLogLoss) || MaxDeltaLogLoss > 0 || (MaxDeltaBrier is { } b && (!double.IsFinite(b) || b > 0)) || !double.IsFinite(MaxEcePp) || MaxEcePp is < 0 or > 100)
        {
            problems.Add("the log loss and Brier limits must be zero or negative and the calibration limit 0 to 100");
        }

        if (string.IsNullOrWhiteSpace(RecipeOptionsHash) || string.IsNullOrWhiteSpace(LabelRuleVersion))
        {
            problems.Add("the recipe options hash and the label rule version must be named");
        }

        return problems;
    }

    /// <summary>The knobs as text, in a fixed order with invariant numbers: part of the trial text.</summary>
    internal string Canonical()
    {
        var c = CultureInfo.InvariantCulture;
        return string.Create(c, $"unit={Unit};train_months={MinTrainMonths};embargo={EmbargoSessions};min_train={MinTrainTrades};take={Take.MinKeptShare:R},{Take.ThresholdStep:R},{Take.MinThresholdRows};block={Block},{BlockSessions},{SensitivityBlockSessions};boot={Draws},{Seed},{Level:R};verdict={MinEvaluableFolds},{MinPairedCandidates},{MinFlips},{MinGainR:R},{PositiveFoldShare:R},{MaxDeltaLogLoss:R},{(MaxDeltaBrier is { } b ? b.ToString("R", c) : "none")},{MaxEcePp:R},{MinHoldOutRows}");
    }
}
