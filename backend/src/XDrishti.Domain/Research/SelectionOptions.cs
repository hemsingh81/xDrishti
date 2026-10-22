namespace XDrishti.Domain.Research;

/// <summary>
/// The inputs of the exit choice that are not data (P4-06, D3): the walk-forward geometry, the objective and the thresholds. Doc 16 <c>walk_forward</c> and <c>selection_objective</c>. All of them are written in the record.
/// </summary>
/// <param name="MinTrainMonths">The first test window starts no earlier than this many months after the first entry (rounded up to a month start).</param>
/// <param name="TestMonths">The length of a test window in calendar months.</param>
/// <param name="EmbargoSessions">The sessions between the entry of a train trade and the start of the test window.</param>
/// <param name="Lambda">The weight of the downside deviation in the score.</param>
/// <param name="MinTrades">The fewest trades with which a candidate takes part in a choice, and the fewest in the test window of a fold for it to count.</param>
/// <param name="PreferPlateau">Prefer a candidate whose neighbours score about as well over the highest score (doc 08 §3).</param>
/// <param name="StableFoldsPct">The share of the folds, in percent, that must agree (and must be positive out of sample) for the choice to be called stable.</param>
public sealed record SelectionOptions(int MinTrainMonths = 9, int TestMonths = 3, int EmbargoSessions = 5, double Lambda = 0.5, int MinTrades = 30, bool PreferPlateau = true, double StableFoldsPct = 60)
{
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

        if (EmbargoSessions is < 0 or > 250)
        {
            problems.Add("embargo_sessions must be 0 to 250");
        }

        if (!double.IsFinite(Lambda) || Lambda < 0 || Lambda > 100)
        {
            problems.Add("lambda must be 0 to 100");
        }

        if (MinTrades < 2)
        {
            problems.Add("min_trades must be at least 2");
        }

        if (!double.IsFinite(StableFoldsPct) || StableFoldsPct is <= 0 or > 100)
        {
            problems.Add("stable_folds_pct must be above 0 and at most 100");
        }

        return problems.AsReadOnly();
    }
}
