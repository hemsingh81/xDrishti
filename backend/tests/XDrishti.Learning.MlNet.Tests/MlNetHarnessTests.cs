using XDrishti.Domain.Learning;

namespace XDrishti.Learning.MlNet.Tests;

/// <summary>The model against the P6-05 harness: noise cannot earn a place, a learnable set beats the baselines fold by fold (P6-06, ADR 0068).</summary>
public sealed class MlNetHarnessTests
{
    private static ValidationReport Run(bool learnable, bool interaction = false) =>
        WalkForwardValidator.Compare(SyntheticRows.Make(1000, learnable, interaction: interaction), [new MlNetScorerFactory()], new ValidationOptions()).Value;

    [Fact]
    public void A_set_with_no_signal_cannot_beat_the_baselines()
    {
        var report = Run(learnable: false);

        report.Verdicts.Single().Outcome.ShouldBe(ValidationOutcome.CannotBeatBaselines);
        report.Verdicts.Single().Name.ShouldBe("meta_model");
        report.Results.Count.ShouldBeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public void A_set_with_an_interaction_that_the_baselines_cannot_see_is_beaten_pooled_and_in_most_folds()
    {
        var report = Run(learnable: true, interaction: true);

        report.Results.Count(f => Candidate(f).LogLoss < f.Scorers.Single(s => s.Name == "logistic").Metrics.LogLoss).ShouldBeGreaterThanOrEqualTo((report.Results.Count * 2) / 3);
        report.Verdicts.Single().PooledBeatsBoth.ShouldBeTrue();
        report.Verdicts.Single().PooledEcePp.ShouldBeLessThan(8);
    }

    private static ProbabilityMetrics Candidate(FoldResult fold) => fold.Scorers.Single(s => s.Name == "meta_model").Metrics;
}
