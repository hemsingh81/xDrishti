using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The eligibility rule of P6-00 D4 as a hand table (P6-05, ADR 0067). Margins: log loss 0.005, Brier 0.002, calibration 5 pp, 2/3 of the folds, 6 folds to be eligible.</summary>
public sealed class EligibilityRuleTests
{
    private static ProbabilityMetrics M(double loss, double brier, double ece = 1) => new(100, 40, loss, brier, 0.6, ece, 1.2);

    private static BaselineComparison Win(double candidateLoss = 0.58, double ece = 1) => new(M(candidateLoss, candidateLoss - 0.39, ece), M(0.60, 0.21), M(0.60, 0.21));

    private static BaselineComparison Lose() => new(M(0.61, 0.22), M(0.60, 0.21), M(0.60, 0.21));

    private static CandidateVerdict Judge(IReadOnlyList<BaselineComparison> folds, BaselineComparison pooled, ValidationOptions? o = null, int train = 500) =>
        EligibilityRule.Judge("m", folds, pooled, train, o ?? new ValidationOptions());

    [Fact]
    public void Beating_both_baselines_in_every_one_of_six_folds_is_eligible()
    {
        var v = Judge(Enumerable.Repeat(Win(), 6).ToArray(), Win());

        v.Outcome.ShouldBe(ValidationOutcome.Eligible);
        v.FoldsNeeded.ShouldBe(4);
        v.Reasons.ShouldBeEmpty();
    }

    [Fact]
    public void The_same_result_on_five_folds_is_capped_at_inconclusive()
    {
        var v = Judge(Enumerable.Repeat(Win(), 5).ToArray(), Win());

        v.Outcome.ShouldBe(ValidationOutcome.Inconclusive);
        v.Reasons.ShouldHaveSingleItem().ShouldContain("only 5 folds");
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(3, false)]
    public void Four_of_six_folds_are_enough_and_three_are_not(int wins, bool eligible)
    {
        var folds = Enumerable.Repeat(Win(), wins).Concat(Enumerable.Repeat(Lose(), 6 - wins)).ToArray();

        Judge(folds, Win()).Outcome.ShouldBe(eligible ? ValidationOutcome.Eligible : ValidationOutcome.CannotBeatBaselines);
    }

    [Fact]
    public void A_gain_smaller_than_the_margin_is_not_a_win()
    {
        var v = Judge(Enumerable.Repeat(Win(0.598), 6).ToArray(), Win(0.598));

        v.Outcome.ShouldBe(ValidationOutcome.CannotBeatBaselines);
        v.FoldsBeatingBoth.ShouldBe(0);
    }

    [Fact]
    public void Beating_only_one_baseline_is_not_enough()
    {
        var oneOnly = new BaselineComparison(M(0.58, 0.20), M(0.60, 0.21), M(0.57, 0.19));

        Judge(Enumerable.Repeat(oneOnly, 6).ToArray(), oneOnly).Outcome.ShouldBe(ValidationOutcome.CannotBeatBaselines);
    }

    [Fact]
    public void Calibration_above_the_limit_and_too_few_train_rows_are_listed()
    {
        var v = Judge(Enumerable.Repeat(Win(), 6).ToArray(), Win(ece: 6.5), train: 20);

        v.Outcome.ShouldBe(ValidationOutcome.CannotBeatBaselines);
        v.Reasons.ShouldContain(r => r.Contains("calibration error 6.50 pp"));
        v.Reasons.ShouldContain(r => r.Contains("trained on 20 rows"));
    }

    [Fact]
    public void More_trials_widen_the_margins()
    {
        // 8 trials: factor 1 + 0.25 * 3 = 1.75, log loss margin 0.00875; a gain of 0.008 passes with one trial only
        var folds = Enumerable.Repeat(Win(0.592), 6).ToArray();

        Judge(folds, Win(0.592)).Outcome.ShouldBe(ValidationOutcome.Eligible);
        Judge(folds, Win(0.592), new ValidationOptions(Trials: 8)).Outcome.ShouldBe(ValidationOutcome.CannotBeatBaselines);
    }
}
