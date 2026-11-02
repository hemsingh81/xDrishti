using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>P13-05 criterion 10 and the verdict table row by row, P13 and P15 presets; an unavailable input never passes.</summary>
public sealed class ExtensionVerdictRuleTests
{
    private static readonly ExtensionComparisonOptions Depth = ExtensionComparisonOptions.ForDepth() with { RecipeOptionsHash = "r", LabelRuleVersion = "tb-1" };

    private static readonly ExtensionComparisonOptions Filings = ExtensionComparisonOptions.ForFilings() with { RecipeOptionsHash = "r", LabelRuleVersion = "tb-1" };

    private static ExtensionVerdictInput Good(double factor = 1) => ExtensionComparisonTests.Keepable(6, factor);

    [Fact]
    public void Every_criterion_met_is_a_keep()
    {
        ExtensionVerdictRule.Decide(Good(), Depth).Verdict.ShouldBe(ExtensionVerdict.Keep);
    }

    [Fact]
    public void Too_few_folds_or_candidates_is_not_evaluable_and_names_each_missing_quantity()
    {
        var result = ExtensionVerdictRule.Decide(Good() with { EvaluableFolds = 4, PairedCandidates = 1_240 }, Depth);

        result.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        result.Missing.ShouldBe(["folds 4 of 6", "paired candidates 1,240 of 2,000"]);
    }

    [Fact]
    public void An_unavailable_lower_bound_is_not_evaluable_and_never_a_pass()
    {
        var stated = ExtensionVerdictRule.Decide(Good() with { Lower = null }, Depth);
        var sensitivity = ExtensionVerdictRule.Decide(Good() with { LowerSensitivity = null }, Depth);

        stated.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        stated.Missing.ShouldBe(["lower bound unavailable"]);
        sensitivity.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
    }

    [Theory]
    [InlineData("flips")]
    [InlineData("lower")]
    [InlineData("sensitivity")]
    [InlineData("logloss")]
    [InlineData("brier")]
    [InlineData("ece")]
    public void One_failed_criterion_is_not_a_keep_and_an_unavailable_one_is_not_a_pass(string which)
    {
        var failing = which switch
        {
            "flips" => Good() with { Flips = 299 },
            "lower" => Good() with { Lower = 0.019 },
            "sensitivity" => Good() with { LowerSensitivity = 0.019 },
            "logloss" => Good() with { DeltaLogLoss = -0.004 },
            "brier" => Good() with { DeltaBrier = -0.001 },
            _ => Good() with { EceBPp = 5.1 },
        };
        var unavailable = which switch
        {
            "logloss" => Good() with { DeltaLogLoss = null },
            "brier" => Good() with { DeltaBrier = null },
            "ece" => Good() with { EceBPp = null },
            _ => failing,
        };

        ExtensionVerdictRule.Decide(failing, Depth).Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        ExtensionVerdictRule.Decide(unavailable, Depth).Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        ExtensionVerdictRule.Decide(failing, Depth).Failed.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_margins_widen_with_the_factor()
    {
        var input = Good() with { Lower = 0.035, LowerSensitivity = 0.035, DeltaLogLoss = -0.0074 };

        ExtensionVerdictRule.Decide(input, Depth).Verdict.ShouldBe(ExtensionVerdict.Keep);
        ExtensionVerdictRule.Decide(input with { Factor = 1.5 }, Depth).Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        ExtensionVerdictRule.Decide(Good(1.5) with { Lower = 0.031, LowerSensitivity = 0.031, DeltaLogLoss = -0.0076 }, Depth).Verdict.ShouldBe(ExtensionVerdict.Keep);
    }

    [Fact]
    public void An_upper_bound_below_the_minimum_gain_is_a_drop_using_the_larger_of_the_two_blocks_and_never_widened()
    {
        var weak = Good() with { Lower = -0.05, LowerSensitivity = -0.06, Upper = 0.019, UpperSensitivity = 0.0195, Factor = 2 };

        ExtensionVerdictRule.Decide(weak, Depth).Verdict.ShouldBe(ExtensionVerdict.Drop);
        ExtensionVerdictRule.Decide(weak with { UpperSensitivity = 0.021 }, Depth).Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        ExtensionVerdictRule.Decide(weak with { UpperSensitivity = null }, Depth).Verdict.ShouldBe(ExtensionVerdict.Inconclusive, "an unavailable upper bound never supports a drop");
        ExtensionVerdictRule.Decide(weak with { Upper = null }, Depth).Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
    }

    [Fact]
    public void The_filings_preset_needs_a_thousand_candidates_no_flips_no_brier_and_only_the_season_block()
    {
        var input = new ExtensionVerdictInput(6, 1_000, 0, 4, 0.03, 0.2, null, null, false, -0.006, null, 4, 1);

        ExtensionVerdictRule.Decide(input, Filings).Verdict.ShouldBe(ExtensionVerdict.Keep);
        ExtensionVerdictRule.Decide(input with { PairedCandidates = 999 }, Filings).Missing.ShouldBe(["paired candidates 999 of 1,000"]);
        ExtensionVerdictRule.Decide(input, Depth).Verdict.ShouldBe(ExtensionVerdict.NotEvaluable, "the depth preset wants 2,000 candidates and a sensitivity bound");
    }

    [Fact]
    public void Six_evaluable_folds_need_four_positive_and_five_need_four_too()
    {
        ExtensionVerdictRule.Decide(Good() with { PositiveFolds = 3 }, Depth).Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        ExtensionVerdictRule.Decide(Good() with { PositiveFolds = 4 }, Depth).Verdict.ShouldBe(ExtensionVerdict.Keep);
        ExtensionVerdictRule.Decide(Good() with { EvaluableFolds = 9, PositiveFolds = 5 }, Depth).Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        ExtensionVerdictRule.Decide(Good() with { EvaluableFolds = 9, PositiveFolds = 6 }, Depth).Verdict.ShouldBe(ExtensionVerdict.Keep);
    }

    [Fact]
    public void The_options_name_their_problems()
    {
        Depth.Problems().ShouldBeEmpty();
        Filings.Problems().ShouldBeEmpty();
        ExtensionComparisonOptions.ForDepth().Problems().ShouldNotBeEmpty("the recipe options hash and the label rule must be named");
        (Depth with { SensitivityBlockSessions = 1 }).Problems().ShouldNotBeEmpty();
        (Depth with { Draws = 10 }).Problems().ShouldNotBeEmpty();
        (Depth with { MaxDeltaLogLoss = 0.01 }).Problems().ShouldNotBeEmpty();
    }
}
