namespace XDrishti.Domain.Learning;

/// <summary>
/// The knobs of the cell statistics (doc 16 <c>learning</c>): the recency half-life, the strength of the Beta prior, the evidence needed and the credible level. Every one is written in the stored snapshot.
/// </summary>
/// <param name="RecencyHalfLifeDays">A trade this many calendar days older than the as-of date counts half as much (doc 16 <c>recency_half_life_days</c>, 180).</param>
/// <param name="PriorStrength">The pseudo-trades of the Beta prior (doc 16 <c>bayes_prior.strength</c>, 20).</param>
/// <param name="MinTrades">A cell window with fewer trades reads "not enough evidence" (doc 16 <c>min_trades</c>, 30).</param>
/// <param name="CredibleLevel">The equal-tailed credible interval is this wide; the lower bound is the confidence (0.90, so the lower bound is the 5 % quantile).</param>
public sealed record CellStatsOptions(double RecencyHalfLifeDays = 180, double PriorStrength = 20, int MinTrades = 30, double CredibleLevel = 0.90)
{
    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!double.IsFinite(RecencyHalfLifeDays) || RecencyHalfLifeDays is < 1 or > 3650)
        {
            problems.Add("recency_half_life_days must be 1 to 3650");
        }

        if (!double.IsFinite(PriorStrength) || PriorStrength is < 0 or > 1000)
        {
            problems.Add("prior_strength must be 0 to 1000");
        }

        if (MinTrades is < 1 or > 100000)
        {
            problems.Add("min_trades must be 1 to 100000");
        }

        if (!double.IsFinite(CredibleLevel) || CredibleLevel is <= 0 or >= 1)
        {
            problems.Add("credible_level must be above 0 and below 1");
        }

        return problems.AsReadOnly();
    }
}
