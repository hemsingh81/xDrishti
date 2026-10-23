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

    private static ValidationRow Make(int i, double score, double r, string cell = "S|15m|BUY|intraday|X|*", int step = 3)
    {
        var date = Start.AddDays(i * step);
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

    /// <summary>Captures the rows each fit is given, in fold order.</summary>
    private sealed class Recording : IScorerFactory
    {
        public List<ValidationRow[]> Fits { get; } = [];

        public string Name => "rec";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train)
        {
            Fits.Add([.. train]);
            return new Read();
        }

        private sealed class Read : IRowScorer
        {
            public double Score(ValidationRow row) => row.Features[0];
        }
    }

    private static (MetaPolicyReport Report, Recording Recorder) RunRecorded(IReadOnlyList<ValidationRow> rows)
    {
        var recorder = new Recording();
        return (MetaPolicyEvaluator.Evaluate(rows, recorder, Policy, Folds).Value, recorder);
    }

    private static long FirstDecision(IReadOnlyList<ValidationRow> rows, ValidationFold fold) =>
        rows.Where(r => r.TradeDate >= fold.TestStart && r.TradeDate <= fold.TestEnd).Min(r => r.DecisionEpochSeconds);

    [Fact]
    public void Every_row_a_fit_sees_is_before_the_embargo_and_resolved_before_the_first_decision_of_the_test_window()
    {
        var rows = Separable(400).Select(r => r with { ResolvedEpochSeconds = r.ResolvedEpochSeconds + (86400L * 20) }).ToList();

        var (report, recorder) = RunRecorded(rows);

        recorder.Fits.Count.ShouldBe(report.Folds.Count);
        for (var f = 0; f < report.Folds.Count; f++)
        {
            var fold = report.Folds[f].Fold;
            var first = FirstDecision(rows, fold);
            recorder.Fits[f].ShouldNotBeEmpty();
            recorder.Fits[f].ShouldAllBe(r => r.TradeDate < fold.TestStart.AddDays(-Folds.EmbargoDays) && r.ResolvedEpochSeconds + HoldOut.BarSeconds < first);
        }

        report.Folds.ShouldContain(f => f.Fold.Purged > 0);
    }

    [Fact]
    public void A_train_row_on_the_embargo_date_is_excluded_and_the_day_before_it_is_kept()
    {
        var rows = Enumerable.Range(0, 480).Select(i => Make(i, i % 5 < 3 ? 0.8 : 0.2, i % 5 < 3 ? 1.5 : -1, step: 1)).ToList();

        var (report, recorder) = RunRecorded(rows);

        for (var f = 0; f < report.Folds.Count; f++)
        {
            var edge = report.Folds[f].Fold.TestStart.AddDays(-Folds.EmbargoDays);
            rows.ShouldContain(r => r.TradeDate == edge);
            recorder.Fits[f].ShouldNotContain(r => r.TradeDate >= edge);
            recorder.Fits[f].ShouldContain(r => r.TradeDate == edge.AddDays(-1));
        }
    }

    [Fact]
    public void Changing_a_folds_own_and_later_labels_leaves_its_train_rows_and_threshold_alone_but_changes_its_totals_and_later_thresholds()
    {
        var (plain, plainFits) = RunRecorded(Separable(400));
        var cut = Start.AddDays(260 * 3);
        var (changed, changedFits) = RunRecorded(Separable(400, i => i >= 260));

        changed.Folds.Count.ShouldBe(plain.Folds.Count);
        var first = plain.Folds.First(f => f.Fold.TestEnd >= cut).Fold.Index;
        first.ShouldBeGreaterThan(1);
        var affectedFolds = 0;
        for (var i = 0; i < plain.Folds.Count; i++)
        {
            var p = plain.Folds[i];
            var c = changed.Folds[i];
            if (p.Fold.Index <= first)
            {
                c.Thresholds.ShouldBe(p.Thresholds);
                changedFits.Fits[i].Select(r => (r.Key, r.Label, r.R)).ShouldBe(plainFits.Fits[i].Select(r => (r.Key, r.Label, r.R)));
            }

            if (p.Fold.Index == first)
            {
                c.Totals.ShouldNotBe(p.Totals);
            }

            if (p.Fold.Index > first && p.Evaluable && !c.Thresholds.SequenceEqual(p.Thresholds))
            {
                affectedFolds++;
            }
        }

        affectedFolds.ShouldBeGreaterThan(0);
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

    private static List<ValidationRow> NegativeNoSkill(int count)
    {
        var random = new Random(11);
        return Enumerable.Range(0, count).Select(i => Make(i, random.NextDouble(), i % 5 == 0 ? 1.5 : -1)).ToList();
    }

    [Fact]
    public void A_no_skill_score_on_a_losing_book_does_not_help_though_skipping_any_share_raises_R_per_candidate()
    {
        var report = Run(NegativeNoSkill(500));

        report.Pooled.BaselinePerCandidate.ShouldBeLessThan(0);
        report.Pooled.Lift.ShouldBeGreaterThan(Policy.MinLiftR);
        report.Pooled.ExcessLift.ShouldBeLessThan(Policy.MinLiftR);
        report.Outcome.ShouldBe(MetaPolicyOutcome.DoesNotHelp);
    }

    [Fact]
    public void The_random_skip_lift_is_minus_the_skipped_share_times_the_take_everything_mean()
    {
        var t = new MetaPolicyTotals(6, 4, 3, -8);

        t.BaselinePerCandidate.ShouldBe(-0.5, 1e-12);
        t.RandomLift.ShouldBe(0.2, 1e-12);
        t.Lift.ShouldBe(0.8, 1e-12);
        t.ExcessLift.ShouldBe(0.6, 1e-12);
        t.Skill.ShouldNotBeNull().ShouldBe(2.5, 1e-12);
    }

    [Fact]
    public void The_skill_standard_error_uses_the_sample_variance_of_each_side()
    {
        var t = MetaPolicyTotals.One(true, 1).Plus(MetaPolicyTotals.One(true, 3)).Plus(MetaPolicyTotals.One(false, -1)).Plus(MetaPolicyTotals.One(false, -3));

        t.SkillStandardError.ShouldNotBeNull().ShouldBe(Math.Sqrt(2), 1e-12);
        MetaPolicyTotals.One(true, 1).SkillStandardError.ShouldBeNull();
    }

    [Fact]
    public void More_trials_widen_the_margins_until_a_real_but_modest_edge_no_longer_passes()
    {
        var rows = Separable(400);
        var knobs = Policy with { MinSkillR = 0.5 };

        Run(rows, knobs).Outcome.ShouldBe(MetaPolicyOutcome.Helps);
        var many = Run(rows, knobs with { Trials = 1 << 20 });

        many.Outcome.ShouldBe(MetaPolicyOutcome.DoesNotHelp);
        many.ToText().ShouldContain("trials 1048576");
        many.Limits.ShouldContain(l => l.Contains("more trials", StringComparison.Ordinal));
    }

    [Fact]
    public void A_difference_within_the_standard_error_bound_does_not_help_even_when_it_clears_the_margins()
    {
        var random = new Random(5);
        var rows = Enumerable.Range(0, 800).Select(i =>
        {
            var high = random.NextDouble() < 0.65;
            var win = random.NextDouble() < (high ? 0.65 : 0.35);
            return Make(i, high ? 0.8 : 0.2, win ? 1.5 : -1);
        }).ToList();

        var loose = Folds with { FoldsBetterShare = 0.01 };

        Run(rows, v: loose).Outcome.ShouldBe(MetaPolicyOutcome.Helps, Run(rows, v: loose).ToText());
        var strict = Run(rows, Policy with { SignificanceZ = 10 }, loose);

        strict.Outcome.ShouldBe(MetaPolicyOutcome.DoesNotHelp);
        strict.Reasons.ShouldContain(r => r.Contains("standard error", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fold_where_every_group_takes_everything_is_not_evaluable_and_says_why()
    {
        var rows = Enumerable.Range(0, 700).Select(i => Make(i, i % 5 < 3 ? 0.8 : 0.2, i % 5 < 3 ? 1.5 : -1, string.Create(CultureInfo.InvariantCulture, $"S|15m|S{i % 40}|intraday|X|*"), step: 1)).ToList();

        var report = Run(rows, Policy with { Split = MetaSplit.Side });

        report.Folds.ShouldAllBe(f => !f.Evaluable);
        report.Folds.Skip(1).ShouldAllBe(f => f.Reason!.Contains("takes everything", StringComparison.Ordinal));
        report.Outcome.ShouldBe(MetaPolicyOutcome.NotEnoughEvidence);
        report.ToText().ShouldContain("nothing was tested");
    }

    [Fact]
    public void A_cell_key_that_cannot_be_read_fails_with_a_code_when_a_split_needs_it_and_not_otherwise()
    {
        var rows = Separable(400).Select((r, i) => i == 5 ? r with { CellKey = "garbage" } : r).ToList();

        MetaPolicyEvaluator.Evaluate(rows, new FeatureScorer(), Policy with { Split = MetaSplit.Side }, Folds).Error.Code.ShouldBe("policy.cell_key");
        MetaPolicyEvaluator.Evaluate(rows, new FeatureScorer(), Policy, Folds).IsSuccess.ShouldBeTrue();
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
