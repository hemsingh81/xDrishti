using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Evaluation;

namespace XDrishti.Domain.Tests.OptionEvaluation;

/// <summary>P14-09 criterion 2 over 200 seeds (1,000 draws each to keep the run short; the default 10,000 is covered by the single-seed tests): a placebo is never a KEEP, a planted effect is a KEEP, and the same effect with too few folds or pairs names what is missing.</summary>
public sealed class ProtocolOPropertyTests
{
    private static readonly ExpressionStudyOptions Quick = ExpressionStudyOptions.Default with { Draws = 1000 };

    [Fact]
    public void A_placebo_with_mean_zero_is_never_a_keep_at_N_1_or_N_16_over_200_seeds()
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var pairs = ExprPairs.Make(seed, ExprPairs.Even(7, 86), 0.0);

            ExprPairs.Run(pairs, 1, Quick).Outcome.Verdict.ShouldNotBe(ExtensionVerdict.Keep, "seed " + seed + " N 1");
            ExprPairs.Run(pairs, 16, Quick).Outcome.Verdict.ShouldNotBe(ExtensionVerdict.Keep, "seed " + seed + " N 16");
        }
    }

    [Fact]
    public void A_planted_mean_of_04_R_over_7_quarters_and_600_pairs_is_a_keep_over_200_seeds_and_the_short_forms_are_NOT_EVALUABLE()
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var keep = ExprPairs.Run(ExprPairs.Make(seed, ExprPairs.Even(7, 86), 0.4), 16, Quick).Outcome;
            keep.Verdict.ShouldBe(ExtensionVerdict.Keep, "seed " + seed + ": " + string.Join("; ", keep.Failed.Concat(keep.Missing)));

            ExprPairs.Run(ExprPairs.Make(seed, [120, 120, 120, 120, 120, 5, 5], 0.4), 16, Quick).Outcome.Missing.ShouldBe(["folds 5 of 6"]);
            ExprPairs.Run(ExprPairs.Make(seed, ExprPairs.Even(7, 50), 0.4), 16, Quick).Outcome.Missing.ShouldBe(["paired trades 350 of 400"]);
        }
    }

    [Fact]
    public void The_row_order_does_not_change_the_record_over_a_few_seeds()
    {
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var pairs = ExprPairs.Make(seed, ExprPairs.Even(7, 86), 0.2);
            var reversed = pairs.AsEnumerable().Reverse().ToList();

            var a = ExprPairs.Run(pairs, 16, Quick).Outcome;
            var b = ExprPairs.Run(reversed, 16, Quick).Outcome;

            b.EstimatesJson.ShouldBe(a.EstimatesJson);
            b.EvidenceJson.ShouldBe(a.EvidenceJson);
        }
    }
}
