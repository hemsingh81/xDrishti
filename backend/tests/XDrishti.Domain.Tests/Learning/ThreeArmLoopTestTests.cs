using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-12 part 12a, AC7 and AC8 (ADR 0218, spec D4): the three arms on the same candidates, the day bootstrap of CF-10 reused, "no difference" an answer, fewer than 20 days inconclusive.</summary>
public sealed class ThreeArmLoopTestTests
{
    private static readonly LoopOptions Options = new();
    private static readonly DateOnly Start = new(2026, 3, 2);

    // a night with a winner (R +1, scores 0.8) and a loser (R -1, scores 0.2) for `days` days
    private static List<LoopCandidate> Nights(int days, double? frozenWinner, double? frozenLoser, double retrainedWinner = 0.8, double retrainedLoser = 0.2) =>
    [
        .. Enumerable.Range(0, days).SelectMany(d => new[]
        {
            new LoopCandidate($"w{d}", Start.AddDays(d), 1, frozenWinner, retrainedWinner),
            new LoopCandidate($"l{d}", Start.AddDays(d), -1, frozenLoser, retrainedLoser),
        }),
    ];

    [Fact]
    public void The_exact_oracle_by_enumeration_of_three_days_gives_23_over_27_and_no_measurable_difference()
    {
        // paired differences in 0.01 units on three days: +10, -5, +20; of the 3^3 = 27 resamples of three days 4 have a non-positive sum
        double[] gains = [0.10, -0.05, 0.20];
        DateOnly[] days = [Start, Start.AddDays(1), Start.AddDays(2)];
        var better = 0;
        var worse = 0;
        for (var a = 0; a < 3; a++)
        {
            for (var b = 0; b < 3; b++)
            {
                for (var c = 0; c < 3; c++)
                {
                    var sum = gains[a] + gains[b] + gains[c];
                    better += sum > 1e-12 ? 1 : 0;
                    worse += sum < -1e-12 ? 1 : 0;
                }
            }
        }

        var result = ThreeArmLoopTest.Compare("x", gains, [0, 0, 0], days, minDays: 2, draws: 20000, level: 0.9, helpsAt: 0.95, hurtsBar: 0.95, seedKey: "oracle");

        better.ShouldBe(23);
        (better / 27.0).ShouldBe(0.8519, 1e-4);
        result.ProbabilityBetter!.Value.ShouldBe(23.0 / 27, 0.015);
        result.ProbabilityWorse!.Value.ShouldBe(worse / 27.0, 0.015);
        result.Verdict.ShouldBe(LoopVerdict.NoMeasurableDifference);
        result.Days.ShouldBe(3);
        result.MeanDifference!.Value.ShouldBe(0.25 / 3, 1e-12);
    }

    [Fact]
    public void Fewer_than_twenty_distinct_days_is_inconclusive()
    {
        var result = ThreeArmLoopTest.Evaluate(Nights(19, 0.9, 0.9), 0.5, 0.5, Options, "s");

        result.RetrainedVsNoMl.Verdict.ShouldBe(LoopVerdict.Inconclusive);
        result.RetrainedVsFrozen.Verdict.ShouldBe(LoopVerdict.Inconclusive);
        result.RetrainedVsNoMl.ProbabilityBetter.ShouldBeNull();
        result.RetrainedVsNoMl.Text.ShouldContain("19");
    }

    [Fact]
    public void A_model_that_takes_the_winners_helps_against_no_model_and_against_a_frozen_model_that_takes_everything()
    {
        // the retrained arm takes only the winners (+1 a night), no model takes both (0 a night), the frozen model scores everything high and takes both
        var result = ThreeArmLoopTest.Evaluate(Nights(30, 0.9, 0.9), 0.5, 0.5, Options, "s");

        result.Candidates.ShouldBe(60);
        result.RetrainedMean!.Value.ShouldBe(0.5, 1e-12);
        result.NoMlMean!.Value.ShouldBe(0, 1e-12);
        result.FrozenMean!.Value.ShouldBe(0, 1e-12);
        result.RetrainedVsNoMl.Verdict.ShouldBe(LoopVerdict.Helps);
        result.RetrainedVsFrozen.Verdict.ShouldBe(LoopVerdict.Helps);
        result.RetrainedVsNoMl.MeanDifference!.Value.ShouldBe(0.5, 1e-12);
        result.RetrainedVsNoMl.ProbabilityBetter.ShouldBe(1);
    }

    [Fact]
    public void A_model_that_takes_the_losers_hurts()
    {
        var result = ThreeArmLoopTest.Evaluate(Nights(30, 0.9, 0.9, retrainedWinner: 0.2, retrainedLoser: 0.8), 0.5, 0.5, Options, "s");

        result.RetrainedVsNoMl.Verdict.ShouldBe(LoopVerdict.Hurts);
        result.RetrainedVsNoMl.ProbabilityBetter.ShouldBe(0);
        result.RetrainedVsNoMl.ProbabilityWorse.ShouldBe(1);
    }

    [Fact]
    public void A_higher_helps_bar_does_not_move_the_fixed_hurts_bar()
    {
        var result = ThreeArmLoopTest.Evaluate(Nights(30, 0.9, 0.9, retrainedWinner: 0.2, retrainedLoser: 0.8), 0.5, 0.5, Options with { HelpsAt = 0.999 }, "s");

        result.RetrainedVsNoMl.Verdict.ShouldBe(LoopVerdict.Hurts);
    }

    [Fact]
    public void Two_arms_that_take_the_same_candidates_show_no_measurable_difference_and_are_never_read_as_harm()
    {
        // the frozen arm scores exactly as the retrained one: every difference is 0, so every resample ties
        var result = ThreeArmLoopTest.Evaluate(Nights(30, 0.8, 0.2), 0.5, 0.5, Options, "s");

        result.RetrainedVsFrozen.Verdict.ShouldBe(LoopVerdict.NoMeasurableDifference);
        result.RetrainedVsFrozen.ProbabilityBetter.ShouldBe(0);
        result.RetrainedVsFrozen.ProbabilityWorse.ShouldBe(0);
        result.RetrainedVsFrozen.MeanDifference.ShouldBe(0);
    }

    [Fact]
    public void A_skipped_candidate_contributes_zero_to_its_arm()
    {
        // the retrained arm skips everything (base rate 0.9): its value is 0 and the difference against no model is minus the mean R
        var candidates = Enumerable.Range(0, 25).Select(d => new LoopCandidate("c" + d, Start.AddDays(d), d % 2 == 0 ? 0.4 : -0.2, null, 0.1)).ToList();

        var result = ThreeArmLoopTest.Evaluate(candidates, null, 0.9, Options, "s");

        result.RetrainedMean.ShouldBe(0);
        result.NoMlMean!.Value.ShouldBe(candidates.Average(c => c.R), 1e-12);
        result.RetrainedVsNoMl.MeanDifference!.Value.ShouldBe(-candidates.Average(c => c.R), 1e-12);
    }

    [Fact]
    public void The_score_at_the_base_rate_is_taken()
    {
        ThreeArmLoopTest.Take(0.5, 0.5).ShouldBeTrue();
        ThreeArmLoopTest.Take(0.4999, 0.5).ShouldBeFalse();
    }

    [Fact]
    public void The_arms_are_compared_on_the_candidates_the_retrained_arm_scored_and_a_frozen_arm_that_missed_one_is_unavailable()
    {
        var candidates = Nights(30, 0.9, 0.9);
        candidates[0] = candidates[0] with { FrozenScore = null };
        candidates.Add(new LoopCandidate("unscored", Start.AddDays(40), 5, 0.9, null));

        var result = ThreeArmLoopTest.Evaluate(candidates, 0.5, 0.5, Options, "s");

        result.Candidates.ShouldBe(60); // the unscored candidate is not part of any arm's set
        result.RetrainedVsFrozen.Verdict.ShouldBe(LoopVerdict.Unavailable);
        result.RetrainedVsFrozen.Text.ShouldStartWith("unavailable:");
        result.FrozenMean.ShouldBeNull();
        result.RetrainedVsNoMl.Verdict.ShouldBe(LoopVerdict.Helps);
    }

    [Fact]
    public void Missing_scores_or_base_rates_are_unavailable_never_a_result()
    {
        var none = ThreeArmLoopTest.Evaluate([.. Enumerable.Range(0, 30).Select(d => new LoopCandidate("c" + d, Start.AddDays(d), 1, null, null))], 0.5, 0.5, Options, "s");
        var noRate = ThreeArmLoopTest.Evaluate(Nights(30, 0.9, 0.9), 0.5, null, Options, "s");
        var noFrozenRate = ThreeArmLoopTest.Evaluate(Nights(30, 0.9, 0.9), null, 0.5, Options, "s");

        none.RetrainedVsNoMl.Verdict.ShouldBe(LoopVerdict.Unavailable);
        none.RetrainedVsFrozen.Verdict.ShouldBe(LoopVerdict.Unavailable);
        noRate.RetrainedVsNoMl.Verdict.ShouldBe(LoopVerdict.Unavailable);
        noFrozenRate.RetrainedVsFrozen.Verdict.ShouldBe(LoopVerdict.Unavailable);
        noFrozenRate.RetrainedVsNoMl.Verdict.ShouldBe(LoopVerdict.Helps);
        default(LoopVerdict).ShouldBe(LoopVerdict.Unavailable);
    }

    [Fact]
    public void The_same_input_gives_the_same_answer_and_a_different_seed_key_may_not()
    {
        var a = ThreeArmLoopTest.Evaluate(Nights(25, 0.9, 0.9), 0.5, 0.5, Options, "s");
        var b = ThreeArmLoopTest.Evaluate(Nights(25, 0.9, 0.9), 0.5, 0.5, Options, "s");

        b.ShouldBe(a);
    }

    [Fact]
    public void Options_out_of_range_are_refused()
    {
        Should.Throw<ArgumentException>(() => ThreeArmLoopTest.Evaluate([], 0.5, 0.5, new LoopOptions(MinDays: 5), "s"));
    }
}
