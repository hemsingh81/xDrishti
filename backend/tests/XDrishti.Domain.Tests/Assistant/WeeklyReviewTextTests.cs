using XDrishti.Domain.Assistant.Text;

namespace XDrishti.Domain.Tests.Assistant;

/// <summary>Spec P9-06 AC 11: the weekly review text, its pinned last line and the sample size of an insight that must reappear.</summary>
public sealed class WeeklyReviewTextTests
{
    private static readonly BriefingFacts Facts = BriefingFixtures.Weekly();

    [Fact]
    public void The_fixed_format_review_ends_with_the_pinned_line_fits_the_limit_and_passes_the_guard()
    {
        var text = DeterministicWeeklyReview.Build(Facts);

        text.ShouldEndWith(BriefingWording.WeeklyFooter);
        TextGuard.WordCount(text).ShouldBeLessThanOrEqualTo(400);
        TextGuard.Check(text, Facts, 400).Findings.ShouldBeEmpty();
        DeterministicWeeklyReview.Build(BriefingFixtures.Weekly()).ShouldBe(text);
    }

    [Fact]
    public void A_rephrased_review_that_keeps_the_sample_size_passes()
    {
        const string text = "Plans were graded: 5 plans, 12 tickets hit the target and 9 the stop.\n- Best strategy orb_15 made 0.31R per trade over 143 trades.\nRegime is unavailable.";

        TextGuard.Check(text, Facts, 400).Findings.ShouldBeEmpty();
    }

    [Fact]
    public void An_insight_whose_sample_size_was_dropped_is_refused()
    {
        const string text = "Best strategy orb_15 made 0.31R per trade.\nRegime is unavailable.";

        TextGuard.Check(text, Facts, 400).Findings.ShouldContain(f => f.Code == GuardCodes.EvidenceDropped && f.Token == "143");
    }

    [Fact]
    public void A_review_with_a_made_up_figure_is_refused()
    {
        const string text = "Best strategy orb_15 made 0.41R per trade over 143 trades.";

        TextGuard.Check(text, Facts, 400).Findings.ShouldContain(f => f.Code == GuardCodes.NumberNotInFacts && f.Token.StartsWith("0.41", StringComparison.Ordinal));
    }
}
