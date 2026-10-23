using System.Globalization;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The meta-labelling policy (P6-13, ADR 0075): thresholds from earlier folds only, skipped-trade R, "does not help" as an answer, determinism and no leak.</summary>
public sealed class MetaPolicyEvaluatorTests
{
    private static readonly DateOnly Start = new(2024, 1, 2);

    private static readonly ValidationOptions Folds = new();

    private static readonly MetaPolicyOptions Policy = new();

    private static long Epoch(DateOnly d) => new DateTimeOffset(d.Year, d.Month, d.Day, 4, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static ValidationRow Make(int i, double score, double r, string cell = "S|15m|BUY|intraday|X|*")
    {
        var date = Start.AddDays(i * 3);
        return new ValidationRow("k" + i.ToString("D4", CultureInfo.InvariantCulture), r > 0 ? 1 : 0, r, date, Epoch(date), Epoch(date) + 3600, cell, [score]);
    }

    private static List<ValidationRow> Separable(int count, Func<int, bool>? flip = null) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var win = i % 5 < 3;
            if (flip?.Invoke(i) == true)
            {
                win = !win;
            }

            return Make(i, win ? 0.8 : 0.2, win ? 1.5 : -1);
        }).ToList();

    private static List<ValidationRow> Noise(int count) =>
        Enumerable.Range(0, count).Select(i => Make(i, unchecked((uint)(i * 2654435761u)) % 1000 / 1000.0, i % 5 < 3 ? 1.5 : -1)).ToList();

    private sealed class FeatureScorer(string name = "m") : IScorerFactory
    {
        public string Name => name;

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Read();

        private sealed class Read : IRowScorer
        {
            public double Score(ValidationRow row) => row.Features[0];
        }
    }

    private static MetaPolicyReport Run(IReadOnlyList<ValidationRow> rows, MetaPolicyOptions? o = null, ValidationOptions? v = null) =>
        MetaPolicyEvaluator.Evaluate(rows, new FeatureScorer(), o ?? Policy, v ?? Folds).Value;

    [Fact]
    public void The_threshold_is_the_lowest_grid_value_that_maximises_net_R_under_the_kept_share_floor()
    {
        var rows = Enumerable.Range(0, 400).Select(i => (i % 3) switch { 0 => Make(i, 0.1, -1), 1 => Make(i, 0.5, 0.2), _ => Make(i, 0.9, 1) }).ToList();

        var report = Run(rows);

        var t = report.Policy.ShouldHaveSingleItem();
        t.Threshold.ShouldBe(0.15);
        t.KeptShare.ShouldBe(2 / 3.0, 0.01);
    }

    [Fact]
    public void A_cut_that_would_keep_less_than_the_floor_is_not_considered()
    {
        var rows = Enumerable.Range(0, 400).Select(i => i % 4 == 0 ? Make(i, 0.9, 1) : Make(i, 0.2, -1)).ToList();

        var report = Run(rows);

        report.Policy.Single().KeptShare.ShouldBeGreaterThanOrEqualTo(0.5);
        report.Policy.Single().Threshold.ShouldBe(0.0);
    }

    [Fact]
    public void Take_everything_wins_when_no_cut_helps()
    {
        var rows = Enumerable.Range(0, 400).Select(i => Make(i, (i % 10) / 10.0, 1)).ToList();

        Run(rows).Policy.Single().Threshold.ShouldBe(0.0);
    }

    [Fact]
    public void A_score_that_separates_winners_from_losers_helps_when_enough_folds_exist()
    {
        var report = Run(Separable(400));

        report.Outcome.ShouldBe(MetaPolicyOutcome.Helps);
        report.UsedFolds.ShouldBeGreaterThanOrEqualTo(6);
        report.Folds[0].Evaluable.ShouldBeFalse();
        report.Pooled.Lift.ShouldBeGreaterThan(0.3);
        report.Pooled.MeanSkipped.ShouldNotBeNull().ShouldBeLessThan(0);
        report.Pooled.MeanKept.ShouldNotBeNull().ShouldBeGreaterThan(0);
    }

    [Fact]
    public void With_fewer_than_six_folds_the_best_answer_is_inconclusive()
    {
        var report = Run(Separable(240));

        report.UsedFolds.ShouldBeLessThan(6);
        report.Outcome.ShouldBe(MetaPolicyOutcome.Inconclusive);
        report.Reasons.ShouldContain(r => r.Contains("usable folds", StringComparison.Ordinal));
    }

    [Fact]
    public void A_score_with_no_skill_does_not_help()
    {
        var report = Run(Noise(400));

        report.Outcome.ShouldBe(MetaPolicyOutcome.DoesNotHelp);
        report.Reasons.ShouldContain(r => r.Contains("Taking everything is as good or better", StringComparison.Ordinal));
    }

    [Fact]
    public void Too_few_evaluable_folds_is_not_enough_evidence()
    {
        var report = Run(Separable(120));

        report.Outcome.ShouldBe(MetaPolicyOutcome.NotEnoughEvidence);
    }

    [Fact]
    public void The_skipped_trades_realised_R_is_reported_per_fold_pooled_and_recent()
    {
        var report = Run(Separable(400));

        report.Folds.Where(f => f.Evaluable).ShouldAllBe(f => f.Totals.Skipped > 0 && f.Totals.MeanSkipped < 0);
        report.Recent.Candidates.ShouldBe(100);
        var text = report.ToText();
        text.ShouldContain("skipped");
        text.ShouldContain("pooled out of sample");
        text.ShouldContain("last 100 evaluated");
        text.ShouldContain("limit:");
    }

    [Fact]
    public void A_folds_threshold_does_not_depend_on_its_own_or_later_labels_scores_or_R()
    {
        var plain = Run(Separable(400));
        var cut = Start.AddDays(260 * 3);
        var changed = Run(Separable(400, i => i >= 260));

        changed.Folds.Count.ShouldBe(plain.Folds.Count);
        var affected = plain.Folds.Where(f => f.Fold.TestStart >= cut.AddDays(-80)).Select(f => f.Fold.Index).ToList();
        var first = plain.Folds.First(f => f.Fold.TestEnd >= cut).Fold.Index;
        first.ShouldBeGreaterThan(1);
        foreach (var f in plain.Folds.Where(f => f.Fold.Index <= first))
        {
            changed.Folds[f.Fold.Index - 1].Thresholds.ShouldBe(f.Thresholds);
        }

        affected.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_train_row_whose_label_horizon_reaches_the_test_window_is_not_used_for_the_threshold()
    {
        var rows = Separable(400);
        var longHorizon = rows.Select(r => r with { ResolvedEpochSeconds = r.ResolvedEpochSeconds + (86400L * 400) }).ToList();

        var plain = Run(rows);
        var report = Run(longHorizon);

        report.Folds.Count(f => f.Evaluable).ShouldBeLessThan(plain.Folds.Count(f => f.Evaluable));
    }

    [Fact]
    public void Rows_in_the_hold_out_are_refused()
    {
        var rows = Separable(400);

        var result = MetaPolicyEvaluator.Evaluate(rows, new FeatureScorer(), Policy, Folds with { HoldOutStart = Start.AddDays(600) });

        result.Error.Code.ShouldBe("validation.holdout_touched");
    }

    [Fact]
    public void The_same_rows_in_any_order_give_the_same_text()
    {
        var rows = Separable(400);

        var a = Run(rows).ToText();
        var b = Run([.. rows.AsEnumerable().Reverse()]).ToText();

        b.ShouldBe(a);
    }

    [Fact]
    public void The_split_gives_a_threshold_per_group()
    {
        var rows = Enumerable.Range(0, 400).Select(i => Make(i, i % 5 < 3 ? 0.8 : 0.2, i % 5 < 3 ? 1.5 : -1, i % 2 == 0 ? "S|15m|BUY|intraday|X|*" : "S|15m|SELL|intraday|X|*")).ToList();

        var report = Run(rows, Policy with { Split = MetaSplit.Side });

        report.Policy.Select(p => p.Group).ShouldBe(["BUY", "SELL"]);
    }

    [Fact]
    public void A_group_with_too_few_scores_takes_everything()
    {
        var rows = Enumerable.Range(0, 400).Select(i => Make(i, 0.5, 1, i == 350 ? "S|15m|SELL|intraday|X|*" : "S|15m|BUY|intraday|X|*")).ToList();

        var report = Run(rows, Policy with { Split = MetaSplit.Side });

        report.Policy.Single(p => p.Group == "SELL").Threshold.ShouldBeNull();
    }

    [Fact]
    public void Bad_input_is_a_failure_with_a_code()
    {
        var rows = Separable(400);

        MetaPolicyEvaluator.Evaluate([], new FeatureScorer(), Policy, Folds).Error.Code.ShouldBe("validation.no_rows");
        MetaPolicyEvaluator.Evaluate([rows[0], rows[0]], new FeatureScorer(), Policy, Folds).Error.Code.ShouldBe("validation.duplicate_key");
        MetaPolicyEvaluator.Evaluate(rows, new FeatureScorer(), Policy with { MinKeptShare = 0 }, Folds).Error.Code.ShouldBe("policy.options");
        MetaPolicyEvaluator.Evaluate(Separable(400).Select(r => r with { Features = [1.5] }).ToList(), new FeatureScorer(), Policy, Folds).Error.Code.ShouldBe("validation.bad_probability");
    }

    [Fact]
    public void The_no_policy_report_says_why()
    {
        var report = MetaPolicyReport.NoPolicy("no champion", Policy, ["advisory"]);

        report.Outcome.ShouldBe(MetaPolicyOutcome.NoPolicy);
        report.ToText().ShouldContain("NoPolicy");
        report.ToText().ShouldContain("no champion");
    }
}
