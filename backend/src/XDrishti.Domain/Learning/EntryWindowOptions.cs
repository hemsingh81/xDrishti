namespace XDrishti.Domain.Learning;

/// <summary>
/// The knobs of the learned entry windows (P6-14, ADR 0069); all are printed with the result.
/// </summary>
/// <param name="BucketMinutes">Width of an entry-time bucket in minutes, counted from the session open (09:15); 30 gives 13 buckets.</param>
/// <param name="MinWindowTrades">A window needs at least this many trades (doc 09 §6.2, doc 16 <c>min_trades</c>), and a cell with fewer has no evidence.</param>
/// <param name="MinLift">The shrunk win rate of the window must exceed the cell's win rate by more than this (absolute, 0.03 is 3 percentage points).</param>
/// <param name="PriorStrength">Pseudo-trades of the prior that pulls a window's win rate toward the cell's own (doc 16 <c>bayes_prior.strength</c>, 20).</param>
/// <param name="MinFoldTrades">A test fold judges a window only with at least this many cell trades inside it and outside it.</param>
/// <param name="MinEvaluableFolds">Fewer evaluable folds than this leave the window inconclusive.</param>
public sealed record EntryWindowOptions(
    int BucketMinutes = 30,
    int MinWindowTrades = 30,
    double MinLift = 0.03,
    double PriorStrength = 20,
    int MinFoldTrades = 5,
    int MinEvaluableFolds = 2)
{
    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (BucketMinutes is < 5 or > 375)
        {
            problems.Add("bucket_minutes must be 5 to 375");
        }

        if (MinWindowTrades is < 1 or > 100000)
        {
            problems.Add("min_window_trades must be 1 to 100000");
        }

        if (!double.IsFinite(MinLift) || MinLift is < 0 or > 1)
        {
            problems.Add("min_lift must be 0 to 1");
        }

        if (!double.IsFinite(PriorStrength) || PriorStrength is < 0 or > 10000)
        {
            problems.Add("prior_strength must be 0 to 10000");
        }

        if (MinFoldTrades is < 1 or > 100000)
        {
            problems.Add("min_fold_trades must be 1 to 100000");
        }

        if (MinEvaluableFolds is < 1 or > 1000)
        {
            problems.Add("min_evaluable_folds must be 1 to 1000");
        }

        return problems;
    }
}
