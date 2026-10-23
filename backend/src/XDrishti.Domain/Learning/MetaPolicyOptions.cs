namespace XDrishti.Domain.Learning;

/// <summary>The knobs of the meta-labelling policy (P6-13, ADR 0075); all are printed with the result.</summary>
/// <param name="MinKeptShare">A threshold must keep at least this share of the candidates (doc 09 section 14: more than half removed is flagged).</param>
/// <param name="ThresholdStep">The grid of thresholds: 0, step, 2 x step ... below 1.</param>
/// <param name="MinThresholdRows">Earlier out-of-fold scores needed to choose a threshold (per group).</param>
/// <param name="MinLiftR">The policy must raise net R per candidate by more than this (R) in a fold and pooled.</param>
/// <param name="MinEvaluableFolds">Fewer evaluable folds than this leave the answer at not enough evidence.</param>
/// <param name="LastTrades">The recent evaluated trades on which kept and skipped are compared (doc 09 section 14: the last 100).</param>
/// <param name="MinSideTrades">The last-trades check needs this many on both sides.</param>
/// <param name="Split">How the threshold is split.</param>
public sealed record MetaPolicyOptions(
    double MinKeptShare = 0.5,
    double ThresholdStep = 0.05,
    int MinThresholdRows = 30,
    double MinLiftR = 0.02,
    int MinEvaluableFolds = 2,
    int LastTrades = 100,
    int MinSideTrades = 5,
    MetaSplit Split = MetaSplit.None)
{
    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!double.IsFinite(MinKeptShare) || MinKeptShare is <= 0 or > 1)
        {
            problems.Add("min_kept_share must be above 0 and at most 1");
        }

        if (!double.IsFinite(ThresholdStep) || ThresholdStep is < 0.01 or > 0.5)
        {
            problems.Add("threshold_step must be 0.01 to 0.5");
        }

        if (MinThresholdRows is < 1 or > 100000)
        {
            problems.Add("min_threshold_rows must be 1 to 100000");
        }

        if (!double.IsFinite(MinLiftR) || MinLiftR is < 0 or > 10)
        {
            problems.Add("min_lift_r must be 0 to 10");
        }

        if (MinEvaluableFolds is < 1 or > 1000)
        {
            problems.Add("min_evaluable_folds must be 1 to 1000");
        }

        if (LastTrades is < 2 or > 100000 || MinSideTrades is < 1 or > 100000)
        {
            problems.Add("last_trades must be 2 to 100000 and min_side_trades 1 to 100000");
        }

        if (!Enum.IsDefined(Split))
        {
            problems.Add("split is not known");
        }

        return problems;
    }
}
