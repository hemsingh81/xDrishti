namespace XDrishti.Domain.Learning;

/// <summary>
/// The knobs of the validation harness (P6-05, ADR 0067): the fold geometry of ADR 0054, the margins of the eligibility rule (P6-00 D4) and the honest-limit constants (P6-00 questions 2 to 4). All are written in the report.
/// </summary>
/// <param name="MinTrainMonths">The first test window starts at least this many months after the first trade (ADR 0054).</param>
/// <param name="TestMonths">The length of a test window in calendar months (quarterly).</param>
/// <param name="EmbargoDays">Train rows that entered within this many calendar days before a window start are dropped (5 sessions, about 7 days); on top of the purge by label horizon.</param>
/// <param name="MinTrainTrades">A fold with fewer train rows is not used, and a candidate needs this many rows in the last used fold (the largest train set, standing for the final model) (doc 16 <c>min_trades</c>).</param>
/// <param name="MinFoldsForVerdict">Fewer usable folds than this cap a passing candidate at inconclusive (P6-00 question 4, as P5-03).</param>
/// <param name="FoldsBetterShare">The share of the usable folds, rounded up, in which the candidate must beat both baselines (2/3: 4 of 6).</param>
/// <param name="LogLossMargin">The log loss must be lower than a baseline's by more than this (times the multiple-testing factor).</param>
/// <param name="BrierMargin">The same for the Brier score.</param>
/// <param name="MaxCalibrationErrorPp">The pooled expected calibration error must not exceed this (doc 16 <c>max_calibration_error_pp</c>, 5).</param>
/// <param name="CalibrationBins">Equal-mass bins of the calibration error.</param>
/// <param name="Trials">How many models and settings have been looked at, for <c>MultipleTesting.Factor</c>; widens the margins.</param>
/// <param name="HoldOutStart">When set, any row on or after this date is refused: the hold-out is not used in development.</param>
public sealed record ValidationOptions(
    int MinTrainMonths = 9,
    int TestMonths = 3,
    int EmbargoDays = 7,
    int MinTrainTrades = 30,
    int MinFoldsForVerdict = ValidationOptions.DefaultMinFolds,
    double FoldsBetterShare = 2.0 / 3.0,
    double LogLossMargin = 0.005,
    double BrierMargin = 0.002,
    double MaxCalibrationErrorPp = 5,
    int CalibrationBins = 10,
    int Trials = 1,
    DateOnly? HoldOutStart = null)
{
    /// <summary>The fewest folds a verdict is worth; a model judged on fewer cannot be champion (<see cref="ChampionRule"/>).</summary>
    public const int DefaultMinFolds = 6;

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

        if (EmbargoDays is < 0 or > 365)
        {
            problems.Add("embargo_days must be 0 to 365");
        }

        if (MinTrainTrades is < 1 or > 100000)
        {
            problems.Add("min_train_trades must be 1 to 100000");
        }

        if (MinFoldsForVerdict is < 1 or > 1000)
        {
            problems.Add("min_folds_for_verdict must be 1 to 1000");
        }

        if (!double.IsFinite(FoldsBetterShare) || FoldsBetterShare is <= 0 or > 1)
        {
            problems.Add("folds_better_share must be above 0 and at most 1");
        }

        if (!double.IsFinite(LogLossMargin) || LogLossMargin < 0 || !double.IsFinite(BrierMargin) || BrierMargin < 0)
        {
            problems.Add("margins must be zero or more");
        }

        if (!double.IsFinite(MaxCalibrationErrorPp) || MaxCalibrationErrorPp is < 0 or > 100)
        {
            problems.Add("max_calibration_error_pp must be 0 to 100");
        }

        if (CalibrationBins is < 1 or > 1000)
        {
            problems.Add("calibration_bins must be 1 to 1000");
        }

        if (Trials < 1)
        {
            problems.Add("trials must be at least 1");
        }

        return problems;
    }
}
