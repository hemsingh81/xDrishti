namespace XDrishti.Domain.Learning;

/// <summary>
/// The rules of an experiment (P6-11, ADR 0072; doc 09 §12). The fold geometry is that of the exit choice (ADR 0054); the required improvement is <c>BaseImprovementR + TrialSlopeR * sqrt(Trials)</c> (doc 09 §12 rule 5). All are printed with the result.
/// </summary>
/// <param name="MinTrainMonths">The first test window starts at least this many months after the first trade (ADR 0054).</param>
/// <param name="TestMonths">The length of a test window in calendar months.</param>
/// <param name="MinFoldTrades">A fold is used only when each side has at least this many trades in its test window.</param>
/// <param name="MinFolds">Fewer used folds cannot say better. Never below <see cref="ValidationOptions.DefaultMinFolds"/>: a command line cannot lower the floor.</param>
/// <param name="FoldsBetterShare">The share of the used folds, rounded up, that must be better (2/3: 4 of 6).</param>
/// <param name="BaseImprovementR">The improvement in mean R per trade needed with a single trial (0.02).</param>
/// <param name="TrialSlopeR">Added per square root of the trials (0.006).</param>
/// <param name="MaxTotalRDrop">The candidate's total R, after the live gap, may fall at most this share below the baseline's (0.10).</param>
/// <param name="ExpectedLiveGapR">What a trade is expected to lose live beyond the replay, in R per trade. An assumption until P10 measures it. Both sides are charged per trade, so a candidate that trades less is charged less.</param>
/// <param name="Trials">The experiments tried in the last 30 days, this one included (the ledger count); the command line can only raise it.</param>
/// <param name="NoHoldOut">True when no hold-out was kept for this experiment: it can then never be called better, at best inconclusive.</param>
/// <param name="HoldOutStart">When set, any trade on or after this date is refused: the hold-out is not used in development.</param>
public sealed record ExperimentOptions(
    int MinTrainMonths = 9,
    int TestMonths = 3,
    int MinFoldTrades = 10,
    int MinFolds = ValidationOptions.DefaultMinFolds,
    double FoldsBetterShare = 2.0 / 3.0,
    double BaseImprovementR = 0.02,
    double TrialSlopeR = 0.006,
    double MaxTotalRDrop = 0.10,
    double ExpectedLiveGapR = 0.02,
    int Trials = 1,
    DateOnly? HoldOutStart = null,
    bool NoHoldOut = false)
{
    /// <summary>The fewest trades a side may have in a fold; a command line can raise it, not lower it.</summary>
    public const int MinFoldTradesFloor = 10;

    /// <summary>The expected live gap in R per trade; a command line can raise it, not lower it (zero would flatter a candidate that trades more).</summary>
    public const double LiveGapFloorR = 0.02;

    /// <summary>The days over which experiments are counted as trials (doc 09 §12 rule 5).</summary>
    public const int TrialWindowDays = 30;

    /// <summary>The improvement in mean R per trade a fold and the pooled sample must show.</summary>
    public double RequiredImprovementR => BaseImprovementR + (TrialSlopeR * Math.Sqrt(Math.Max(1, Trials)));

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (MinTrainMonths is < 1 or > 120)
        {
            problems.Add("min_train_months must be 1 to 120");
        }

        if (TestMonths is < 1 or > 60)
        {
            problems.Add("test_months must be 1 to 60");
        }

        if (MinFoldTrades is < 1 or > 100000)
        {
            problems.Add("min_fold_trades must be 1 to 100000");
        }

        if (MinFolds is < 1 or > 1000)
        {
            problems.Add("min_folds must be 1 to 1000");
        }

        if (!double.IsFinite(FoldsBetterShare) || FoldsBetterShare is <= 0 or > 1)
        {
            problems.Add("folds_better_share must be above 0 and at most 1");
        }

        if (!double.IsFinite(BaseImprovementR) || BaseImprovementR < 0 || !double.IsFinite(TrialSlopeR) || TrialSlopeR < 0)
        {
            problems.Add("the improvement margins must be zero or more");
        }

        if (!double.IsFinite(MaxTotalRDrop) || MaxTotalRDrop is < 0 or > 1)
        {
            problems.Add("max_total_r_drop must be 0 to 1");
        }

        if (!double.IsFinite(ExpectedLiveGapR) || ExpectedLiveGapR is < 0 or > 5)
        {
            problems.Add("expected_live_gap_r must be 0 to 5");
        }

        if (Trials < 1)
        {
            problems.Add("trials must be at least 1");
        }

        return problems;
    }
}
