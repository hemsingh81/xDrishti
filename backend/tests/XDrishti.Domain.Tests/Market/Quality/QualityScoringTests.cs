using XDrishti.Domain.Market.Quality;

namespace XDrishti.Domain.Tests.Market.Quality;

public sealed class QualityScoringTests
{
    private static readonly DateOnly Day = new(2026, 10, 9);

    private static QualityChecks Checks(
        string completeness = CheckStatus.Pass,
        string sanity = CheckStatus.Pass,
        string spikes = CheckStatus.Pass,
        string aggregates = CheckStatus.Pass,
        string freshness = CheckStatus.Pass,
        int expected = 1875,
        int found = 1875) =>
        new(
            new CompletenessCheck(completeness, expected, found, expected - found, 0, 0, []),
            new SanityCheck(sanity, 100, sanity == CheckStatus.Pass ? 0 : 1, []),
            new SpikeCheck(spikes, spikes == CheckStatus.Pass ? 0 : 1, 0, 0, 1, []),
            new AggregatesCheck(aggregates, 10, aggregates == CheckStatus.Pass ? 0 : 1, new Dictionary<string, TimeframeCheck>()),
            new FreshnessCheck(freshness, null, Day, freshness == CheckStatus.Pass ? 0 : 1),
            OfficialDailyCheck.NotAvailable,
            null);

    [Fact]
    public void Everything_passing_scores_one_hundred_and_passes()
    {
        var checks = Checks();

        (QualityScoring.Status(checks), QualityScoring.Score(checks)).ShouldBe((CheckStatus.Pass, 100));
    }

    [Theory]
    [InlineData(CheckStatus.Warning, CheckStatus.Pass, CheckStatus.Pass, CheckStatus.Pass, CheckStatus.Warning)]
    [InlineData(CheckStatus.Pass, CheckStatus.Warning, CheckStatus.Pass, CheckStatus.Pass, CheckStatus.Warning)]
    [InlineData(CheckStatus.Pass, CheckStatus.Pass, CheckStatus.Fail, CheckStatus.Pass, CheckStatus.Fail)]
    [InlineData(CheckStatus.Pass, CheckStatus.Pass, CheckStatus.Pass, CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData(CheckStatus.Warning, CheckStatus.Warning, CheckStatus.Fail, CheckStatus.Pass, CheckStatus.Fail)]
    public void The_status_is_the_worst_of_the_checks(string completeness, string spikes, string sanity, string aggregates, string expected)
    {
        QualityScoring.Status(Checks(completeness: completeness, spikes: spikes, sanity: sanity, aggregates: aggregates)).ShouldBe(expected);
    }

    [Fact]
    public void A_stale_instrument_fails_and_loses_the_freshness_weight()
    {
        var checks = Checks(freshness: CheckStatus.Fail);

        (QualityScoring.Status(checks), QualityScoring.Score(checks)).ShouldBe((CheckStatus.Fail, 85));
    }

    [Fact]
    public void A_failed_aggregate_check_loses_its_weight_a_failed_sanity_check_its_own()
    {
        QualityScoring.Score(Checks(aggregates: CheckStatus.Fail)).ShouldBe(75);
        QualityScoring.Score(Checks(sanity: CheckStatus.Fail)).ShouldBe(80);
    }

    [Fact]
    public void A_spike_warning_costs_half_the_spike_weight()
    {
        QualityScoring.Score(Checks(spikes: CheckStatus.Warning)).ShouldBe(95);
    }

    [Fact]
    public void Completeness_earns_the_share_of_the_expected_minutes_that_are_there()
    {
        QualityScoring.Score(Checks(completeness: CheckStatus.Warning, found: 1500, expected: 1875)).ShouldBe(94); // 30 * 0.8 = 24 of 30
        QualityScoring.Score(Checks(completeness: CheckStatus.Warning, found: 0, expected: 1875)).ShouldBe(70);
    }

    [Fact]
    public void A_window_with_nothing_expected_earns_the_full_completeness_weight()
    {
        QualityScoring.Score(Checks(expected: 0, found: 0)).ShouldBe(100);
    }

    [Fact]
    public void The_official_daily_comparison_that_is_not_available_changes_neither_status_nor_score()
    {
        var checks = Checks() with { OfficialDaily = OfficialDailyCheck.NotAvailable };

        checks.OfficialDaily.Status.ShouldBe(CheckStatus.NotAvailable);
        (QualityScoring.Status(checks), QualityScoring.Score(checks)).ShouldBe((CheckStatus.Pass, 100));
    }

    [Fact]
    public void The_weights_add_up_to_one_hundred()
    {
        (QualityScoring.CompletenessWeight + QualityScoring.AggregatesWeight + QualityScoring.SanityWeight + QualityScoring.FreshnessWeight + QualityScoring.SpikesWeight).ShouldBe(100);
    }

    [Fact]
    public void A_status_is_worse_when_it_ranks_higher_and_not_available_never_wins()
    {
        CheckStatus.Worst(CheckStatus.Pass, CheckStatus.Warning).ShouldBe(CheckStatus.Warning);
        CheckStatus.Worst(CheckStatus.Fail, CheckStatus.Warning).ShouldBe(CheckStatus.Fail);
        CheckStatus.Worst(CheckStatus.Pass, CheckStatus.NotAvailable).ShouldBe(CheckStatus.Pass);
        CheckStatus.IsKnownResult(CheckStatus.NotAvailable).ShouldBeFalse();
    }
}
