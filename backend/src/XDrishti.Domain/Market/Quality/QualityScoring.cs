namespace XDrishti.Domain.Market.Quality;

/// <summary>
/// The score (0–100) and the status of an instrument from its checks. Status: the worst of completeness, sanity, spikes, aggregates and freshness (official daily is not available and
/// does not count). Score: weights completeness 30, aggregates 25, sanity 20, freshness 15, spikes 10; a check that fails earns nothing, completeness earns its coverage, a spike warning half.
/// </summary>
public static class QualityScoring
{
    public const int CompletenessWeight = 30;
    public const int AggregatesWeight = 25;
    public const int SanityWeight = 20;
    public const int FreshnessWeight = 15;
    public const int SpikesWeight = 10;

    public static string Status(QualityChecks checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var status = CheckStatus.Pass;
        foreach (var own in new[] { checks.Completeness.Status, checks.Sanity.Status, checks.Spikes.Status, checks.Aggregates.Status, checks.Freshness.Status })
        {
            status = CheckStatus.Worst(status, own);
        }

        return status;
    }

    public static int Score(QualityChecks checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var coverage = checks.Completeness.ExpectedMinutes == 0 ? 1.0 : (double)checks.Completeness.FoundMinutes / checks.Completeness.ExpectedMinutes;
        var points =
            CompletenessWeight * coverage
            + AggregatesWeight * Earned(checks.Aggregates.Status)
            + SanityWeight * Earned(checks.Sanity.Status)
            + FreshnessWeight * Earned(checks.Freshness.Status)
            + SpikesWeight * (checks.Spikes.Status == CheckStatus.Pass ? 1.0 : 0.5);
        return (int)Math.Clamp(Math.Round(points, MidpointRounding.AwayFromZero), 0, 100);
    }

    private static double Earned(string status) => status == CheckStatus.Fail ? 0.0 : 1.0;
}
