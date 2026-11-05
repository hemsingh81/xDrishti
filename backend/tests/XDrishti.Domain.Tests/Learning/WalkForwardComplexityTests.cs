using System.Globalization;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The validation report carries the complexity ladder, the feature shift and the direction checks, and only reports (CF-10 part 10b, ADR 0215).</summary>
public sealed class WalkForwardComplexityTests
{
    private static readonly DateOnly Start = new(2024, 1, 2);
    private static readonly LadderOptions Ladder = new(MinRows: 5);

    /// <summary>Scores 0.5 and explains: feature 0 pushes up with its value, feature 1 pushes up or down by the parity of the train size (so its direction differs from fold to fold), feature 2 never varies.</summary>
    private sealed class Explaining : IScorerFactory
    {
        public string Name => "explaining";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Model(train.Count % 2 == 0 ? 1 : -1);

        private sealed class Model(int sign) : IExplainableScorer
        {
            public double Score(ValidationRow row) => 0.5;

            public IReadOnlyList<FeatureContribution> Explain(ValidationRow row, int top) =>
                [.. new[] { new FeatureContribution(0, row.Features[0]), new FeatureContribution(1, sign * row.Features[1]), new FeatureContribution(2, row.Features[2]) }.Where(c => c.Value != 0).Take(top)];
        }
    }

    private sealed class Plain : IScorerFactory
    {
        public string Name => "plain";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Fixed();

        private sealed class Fixed : IRowScorer
        {
            public double Score(ValidationRow row) => 0.5;
        }
    }

    private static List<ValidationRow> Data() =>
        [.. Enumerable.Range(0, 240).Select(i =>
        {
            var date = Start.AddDays(i * 3);
            var epoch = new DateTimeOffset(date.Year, date.Month, date.Day, 4, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            var label = i % 3 == 0 ? 1 : 0;
            return new ValidationRow("k" + i.ToString("D4", CultureInfo.InvariantCulture), label, label == 1 ? 1.5 : -1, date, epoch, epoch + 3600, i % 2 == 0 ? "A" : "B", [label + (i % 7), 1 + (i % 5), 7]);
        })];

    [Fact]
    public void Without_the_setting_or_with_it_off_nothing_is_added_and_the_text_is_unchanged()
    {
        var none = WalkForwardValidator.Compare(Data(), [new Explaining()], new ValidationOptions()).Value;
        var off = WalkForwardValidator.Compare(Data(), [new Explaining()], new ValidationOptions(Ladder: new LadderOptions(ConfidenceMode.Off))).Value;
        var on = WalkForwardValidator.Compare(Data(), [new Explaining()], new ValidationOptions(Ladder: Ladder)).Value;

        none.Complexity.ShouldBeNull();
        off.Complexity.ShouldBeNull();
        off.ToText().ShouldBe(none.ToText());
        on.Complexity.ShouldNotBeNull();
        on.ToText().Replace(on.Complexity!.ToText(), string.Empty, StringComparison.Ordinal).ShouldBe(none.ToText());
        on.Verdicts.Select(v => (v.Outcome, v.FoldsBeatingBoth, string.Join('|', v.Reasons))).ShouldBe(none.Verdicts.Select(v => (v.Outcome, v.FoldsBeatingBoth, string.Join('|', v.Reasons))));
        on.Pooled.Select(p => p.Metrics).ShouldBe(none.Pooled.Select(p => p.Metrics));
    }

    [Fact]
    public void The_events_are_the_smaller_class_of_the_last_train_set_and_the_ladder_lists_the_logistic_baseline_and_the_candidate()
    {
        var report = WalkForwardValidator.Compare(Data(), [new Explaining()], new ValidationOptions(Ladder: Ladder)).Value;
        var c = report.Complexity!;

        c.TrainRows.ShouldBe(report.Results[^1].Fold.TrainCount);
        c.Wins.ShouldBeGreaterThan(0);
        (c.Wins + c.Losses).ShouldBe(c.TrainRows);
        c.Events.ShouldBe(Math.Min(c.Wins, c.Losses));
        c.Cap.ShouldBe(Math.Min(c.Events / 15, 8));
        c.Folds.ShouldBe(report.Results.Count);
        c.Ladder.Select(a => (a.Scorer, a.Rung)).ShouldBe([("logistic", LadderRung.Logistic), ("explaining", LadderRung.Trees)]);
        c.Ladder.ShouldAllBe(a => !a.Earned && a.FeatureCount == 3 && a.ProbabilityBeatsBelow == null);
        c.Ladder[1].Reasons[0].ShouldContain("not available");
        c.Ladder[1].Reasons[^1].ShouldContain("no monotone constraints"); // D2's rung 4 is monotone trees of depth 3, which no candidate of the harness is yet
        c.Ladder[0].Reasons.ShouldNotContain(r => r.Contains("monotone", StringComparison.Ordinal));
    }

    [Fact]
    public void The_direction_of_each_feature_follows_the_scorer_fold_by_fold()
    {
        var report = WalkForwardValidator.Compare(Data(), [new Explaining()], new ValidationOptions(Ladder: Ladder)).Value;
        var s = report.Complexity!.Stability.Single();

        s.Explained.ShouldBeTrue();
        s.Features.Count.ShouldBe(3);
        s.Features[0].Signs.ShouldBe([.. report.Results.Select(_ => 1)]);
        s.Features[0].Verdict.ShouldBe(StabilityVerdict.Stable);
        s.Features[1].Signs.ShouldBe([.. report.Results.Select(r => r.Fold.TrainCount % 2 == 0 ? 1 : -1)]);
        s.Features[2].Signs.ShouldAllBe(x => x == 0);
        s.Features[2].Verdict.ShouldBe(StabilityVerdict.Unavailable);
    }

    [Fact]
    public void A_scorer_that_cannot_explain_leaves_every_feature_unavailable_and_says_so()
    {
        var report = WalkForwardValidator.Compare(Data(), [new Plain()], new ValidationOptions(Ladder: Ladder)).Value;
        var s = report.Complexity!.Stability.Single();

        s.Explained.ShouldBeFalse();
        s.Features.ShouldAllBe(f => f.Verdict == StabilityVerdict.Unavailable);
        report.ToText().ShouldContain("direction plain: not computed");
    }

    [Fact]
    public void A_feature_that_never_varies_has_no_shift_and_the_others_are_read_in_the_worst_fold()
    {
        var report = WalkForwardValidator.Compare(Data(), [new Plain()], new ValidationOptions(Ladder: Ladder)).Value;
        var shifts = report.Complexity!.Shifts;

        shifts.Count.ShouldBe(3);
        shifts[2].MaxPsi.ShouldBeNull();
        shifts[2].Level.ShouldBeNull();
        shifts[0].MaxPsi.ShouldNotBeNull();
        shifts[0].Fold.ShouldNotBeNull();
        report.Results.Select(r => r.Fold.Index).ShouldContain(shifts[0].Fold!.Value);
        shifts[0].Level.ShouldBe(Psi.Level(shifts[0].MaxPsi!.Value, Ladder));
    }

    [Fact]
    public void The_text_names_the_features_when_names_are_given_and_repeats_on_the_same_rows()
    {
        var one = WalkForwardValidator.Compare(Data(), [new Explaining()], new ValidationOptions(Ladder: Ladder)).Value;
        var two = WalkForwardValidator.Compare(Data(), [new Explaining()], new ValidationOptions(Ladder: Ladder)).Value;

        two.ToText().ShouldBe(one.ToText());
        one.ToText().ShouldContain("complexity (evidence only, no verdict reads it); settings ReportOnly/epv=15");
        one.ToText().ShouldContain(Ladder.KeyText());
        one.ToText().ShouldContain("the worst of 5 folds; bins: up to 10, at least 20 rows per side for each bin");
        one.ToText().ShouldContain("rung 4 Trees, 3 features: not earned");
        one.ToText(["alpha", "beta", "gamma"]).ShouldNotBe(one.ToText());
    }

    [Fact]
    public void Rows_with_no_features_give_the_ladder_and_no_feature_lines()
    {
        var rows = Data().Select(r => r with { Features = [] }).ToList();
        var report = WalkForwardValidator.Compare(rows, [new Plain()], new ValidationOptions(Ladder: Ladder)).Value;

        report.Complexity!.Shifts.ShouldBeEmpty();
        report.Complexity.Stability.Single().Features.ShouldBeEmpty();
        report.Complexity.Ladder.ShouldAllBe(a => a.FeatureCount == 0);
    }

    [Fact]
    public void A_bad_ladder_setting_is_a_validation_problem()
    {
        new ValidationOptions(Ladder: new LadderOptions(Epv: 1)).Problems().ShouldNotBeEmpty();
        WalkForwardValidator.Compare(Data(), [new Plain()], new ValidationOptions(Ladder: new LadderOptions(Epv: 1))).IsFailure.ShouldBeTrue();
    }
}
