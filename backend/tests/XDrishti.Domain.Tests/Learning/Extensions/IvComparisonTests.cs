using XDrishti.Domain.Learning.Extensions;
using static XDrishti.Domain.Tests.Learning.Extensions.ExtensionKit;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>P14-11 criterion 4: <c>ext:iv-1</c> through the P13-05 core with <see cref="ExtensionComparisonOptions.ForOptions"/> (calendar quarters, 12 months of training, 5 sessions of embargo, day blocks with the 5-session sensitivity): placebo features are not kept at N = 1 and 16, a planted feature is kept at seven test quarters and not evaluable at six.</summary>
public sealed class IvComparisonTests
{
    private static readonly DateOnly Start = new(2024, 1, 1);

    private static ExtensionComparisonOptions Options() => ExtensionComparisonOptions.ForOptions() with { RecipeOptionsHash = "r1", LabelRuleVersion = "tb-1", Draws = 1000 };

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public void Placebo_features_are_not_kept_for_200_seeds(int trials)
    {
        var to = new DateOnly(2026, 9, 30);
        var sessions = Sessions(Start, to.AddDays(10));
        var recipe = new ProbeFactory(0, 1);
        var options = Options() with { Draws = 300 };
        for (var seed = 0; seed < 200; seed++)
        {
            var rows = Synthetic(seed, Start, to, 6, Mode.Placebo);

            Compare(rows, Group, recipe, options, sessions, trials).Verdict.ShouldNotBe(ExtensionVerdict.Keep, "seed " + seed);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public void A_planted_feature_is_kept_with_seven_test_quarters(int trials)
    {
        var to = new DateOnly(2026, 9, 30);
        var rows = Synthetic(3, Start, to, 6, Mode.Planted);

        var report = Compare(rows, Group, new ProbeFactory(0, 1), Options(), Sessions(Start, to.AddDays(10)), trials);

        report.FoldsUsed.ShouldBe(7);
        report.FoldsEvaluable.ShouldBe(6);
        report.Options.Unit.ShouldBe(FoldUnit.Quarter);
        report.Verdict.ShouldBe(ExtensionVerdict.Keep, string.Join("; ", report.Failed.Concat(report.Missing)));
    }

    [Fact]
    public void With_six_test_quarters_the_same_planted_feature_is_not_evaluable_and_names_folds_five_of_six()
    {
        var to = new DateOnly(2026, 6, 30);
        var rows = Synthetic(3, Start, to, 6, Mode.Planted);

        var report = Compare(rows, Group, new ProbeFactory(0, 1), Options(), Sessions(Start, to.AddDays(10)));

        report.FoldsUsed.ShouldBe(6);
        report.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        report.Missing.ShouldContain("folds 5 of 6");
    }

    [Fact]
    public void A_trial_of_the_options_preset_is_another_trial_than_the_depth_preset_of_the_same_rows()
    {
        var rows = Synthetic(3, Start, new DateOnly(2025, 6, 30), 2, Mode.Planted);
        var from = rows.Min(r => r.TradeDate);
        var to = rows.Max(r => r.TradeDate);

        var options = ExtensionTrial.Of(Group, "probe", Options(), from, to).Hash;
        var depth = ExtensionTrial.Of(Group, "probe", Opts(), from, to).Hash;

        options.ShouldNotBe(depth);
    }
}
