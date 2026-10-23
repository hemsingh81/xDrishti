using System.Globalization;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The frozen-model counterfactual (P6-12, ADR 0077): same trades on both sides, the frozen side never sees later labels, noise gives no difference, determinism.</summary>
public sealed class CounterfactualEvaluatorTests
{
    private static readonly DateOnly Start = new(2024, 1, 2);

    private static readonly DateOnly Flip = new(2024, 12, 1);

    private static readonly ValidationOptions Folds = new();

    private static readonly CounterfactualOptions Options = new();

    private static long Epoch(DateOnly d) => new DateTimeOffset(d.Year, d.Month, d.Day, 4, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static double Feature(int i) => unchecked((uint)(i * 2654435761u)) % 1000 / 1000.0;

    private static ValidationRow Make(int i, bool win, double feature, int step = 2)
    {
        var date = Start.AddDays(i * step);
        return new ValidationRow($"S|NSE:S{i % 7}|BUY|{date:yyyy-MM-dd}|{i}", win ? 1 : 0, win ? 1.5 : -1, date, Epoch(date), Epoch(date) + 3600, "S|15m|BUY|intraday|X|*", [feature]);
    }

    /// <summary>Label follows the feature; after <see cref="Flip"/> the relationship is reversed.</summary>
    private static List<ValidationRow> Regime(int count, DateOnly? flip = null) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var f = Feature(i);
            var date = Start.AddDays(i * 2);
            var win = f > 0.5;
            if (date >= (flip ?? Flip))
            {
                win = !win;
            }

            return Make(i, win, f);
        }).ToList();

    private static List<ValidationRow> RandomLabels(int count) =>
        Enumerable.Range(0, count).Select(i => Make(i, unchecked((uint)((i + 17) * 40503u)) % 100 < 50, Feature(i))).ToList();

    /// <summary>Learns the direction of the feature from the most recent 30 train rows: right in a stable regime, wrong the moment it flips until it has seen the flip.</summary>
    private sealed class Recent(List<Fit>? log = null) : IScorerFactory
    {
        public string Name => "recent";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train)
        {
            var last = train.OrderBy(r => r.DecisionEpochSeconds).TakeLast(30).ToList();
            var up = last.Where(r => r.Label == 1).Select(r => r.Features[0]).DefaultIfEmpty(0.5).Average();
            var down = last.Where(r => r.Label == 0).Select(r => r.Features[0]).DefaultIfEmpty(0.5).Average();
            var fit = new Fit(train.Select(r => r.Key + ":" + r.Label).ToList(), up > down, []);
            log?.Add(fit);
            return fit;
        }
    }

    private sealed record Fit(List<string> Train, bool Positive, Dictionary<string, double> Scored) : IRowScorer
    {
        public double Score(ValidationRow row)
        {
            var f = row.Features[0];
            var p = Positive ? 0.2 + (0.6 * f) : 0.8 - (0.6 * f);
            Scored[row.Key] = p;
            return p;
        }
    }

    private sealed class ByHash : IScorerFactory
    {
        public string Name => "hash";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Hash();

        private sealed class Hash : IRowScorer
        {
            public double Score(ValidationRow row) => 0.3 + (0.4 * (unchecked((uint)(row.Key.GetHashCode(StringComparison.Ordinal) * 7)) % 100 / 100.0));
        }
    }

    private sealed class BaseRate : IScorerFactory
    {
        public string Name => "rate";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Constant(train.Average(r => r.Label));

        private sealed class Constant(double p) : IRowScorer
        {
            public double Score(ValidationRow row) => p;
        }
    }

    /// <summary>Good when fitted on no more than the first fit's rows (the frozen model), inverted when it has more: retraining hurts.</summary>
    private sealed class Spoiled : IScorerFactory
    {
        private int? _first;

        public string Name => "spoiled";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train)
        {
            _first ??= train.Count;
            return new Fit([], train.Count <= _first, []);
        }
    }

    private static CounterfactualReport Run(IReadOnlyList<ValidationRow> rows, IScorerFactory? model = null, CounterfactualOptions? o = null, ValidationOptions? v = null) =>
        CounterfactualEvaluator.Evaluate(rows, model ?? new Recent(), o ?? Options, v ?? Folds).Value;

    [Fact]
    public void Retraining_helps_when_the_relationship_moves_after_the_cut()
    {
        var report = Run(Regime(340));

        report.Outcome.ShouldBe(CounterfactualOutcome.RetrainingHelps);
        report.Cut.ShouldBe(new DateOnly(2024, 11, 1));
        report.Windows.Count.ShouldBe(4);
        report.Windows[0].Informative.ShouldBeFalse();
        report.Windows.Skip(1).ShouldAllBe(w => w.Informative);
        report.Judged!.MeanGain.ShouldBeGreaterThan(0.1);
        report.Judged.Retrained.Policy.PolicyPerCandidate.ShouldBeGreaterThan(report.Judged.Frozen.Policy.PolicyPerCandidate);
    }

    [Fact]
    public void Both_sides_score_exactly_the_same_trades_on_the_same_days_and_instruments()
    {
        var log = new List<Fit>();
        var rows = Regime(340);

        var report = Run(rows, new Recent(log));

        var inPeriod = rows.Where(r => r.TradeDate >= report.Cut && r.TradeDate <= report.Windows[^1].End).Select(r => r.Key).Order().ToList();
        log[0].Scored.Keys.Order().ToList().ShouldBe(inPeriod);
        var retrainedKeys = log.Skip(1).SelectMany(f => f.Scored.Keys).Concat(report.Windows[0].Rows > 0 ? rows.Where(r => r.TradeDate >= report.Windows[0].Start && r.TradeDate <= report.Windows[0].End).Select(r => r.Key) : []).Order().ToList();
        retrainedKeys.ShouldBe(inPeriod);
        report.Windows.Sum(w => w.Rows).ShouldBe(inPeriod.Count);
        report.Windows.ShouldAllBe(w => w.Days > 0 && w.Instruments == 7);
        report.All!.Rows.ShouldBe(inPeriod.Count);
        report.All.Frozen.Metrics.N.ShouldBe(report.All.Retrained.Metrics.N);
    }

    [Fact]
    public void The_frozen_model_is_fitted_once_on_rows_known_before_the_cut_with_the_embargo_and_the_purge()
    {
        var log = new List<Fit>();
        var rows = Regime(340);
        var cut = new DateOnly(2024, 11, 1);
        var firstDecision = rows.Where(r => r.TradeDate >= cut).Min(r => r.DecisionEpochSeconds);
        var late = rows.First(r => r.TradeDate == cut.AddDays(-10));
        rows[rows.IndexOf(late)] = late with { ResolvedEpochSeconds = firstDecision + 100000 };

        var report = Run(rows, new Recent(log));

        var expected = rows.Where(r => r.TradeDate < cut.AddDays(-7) && r.ResolvedEpochSeconds + 300 < firstDecision).Select(r => r.Key + ":" + r.Label).ToList();
        log[0].Train.ShouldBe(expected);
        log[0].Train.ShouldNotContain(late.Key + ":" + late.Label);
        report.FrozenTrainRows.ShouldBe(expected.Count);
        report.Windows.ShouldAllBe(w => w.FrozenTrainRows == expected.Count);
    }

    [Fact]
    public void Changing_the_labels_after_the_cut_leaves_the_frozen_side_unchanged_but_moves_the_retrained_one()
    {
        var cut = new DateOnly(2024, 11, 1);
        var logA = new List<Fit>();
        var logB = new List<Fit>();
        var rows = Regime(340);
        var changed = rows.Select(r => r.TradeDate >= cut ? r with { Label = 1 - r.Label, R = -r.R } : r).ToList();

        Run(rows, new Recent(logA));
        Run(changed, new Recent(logB));

        logA[0].Train.ShouldBe(logB[0].Train);
        logA[0].Scored.ShouldBe(logB[0].Scored);
        logA.Count.ShouldBe(logB.Count);
        logA[2].Train.ShouldNotBe(logB[2].Train);
        logA[2].Positive.ShouldNotBe(logB[2].Positive);
    }

    [Fact]
    public void Noise_gives_no_difference()
    {
        var report = Run(RandomLabels(340), new BaseRate());

        report.Outcome.ShouldBe(CounterfactualOutcome.NoDifference);
        report.Reasons.ShouldHaveSingleItem().ShouldContain("not told apart");
    }

    [Fact]
    public void The_same_scores_on_both_sides_give_no_difference_and_a_zero_gain()
    {
        var report = Run(Regime(340), new ByHash());

        report.Outcome.ShouldBe(CounterfactualOutcome.NoDifference);
        report.Judged!.MeanGain.ShouldBe(0, 1e-12);
    }

    [Fact]
    public void The_frozen_model_can_be_the_better_one()
    {
        var report = Run(Regime(340, flip: new DateOnly(2100, 1, 1)), new Spoiled());

        report.Outcome.ShouldBe(CounterfactualOutcome.FrozenBetter);
        report.Judged!.MeanGain.ShouldBeLessThan(0);
    }

    [Fact]
    public void Fewer_judged_windows_than_the_minimum_is_inconclusive_whatever_the_numbers()
    {
        var report = Run(Regime(340), o: Options with { FrozenMonths = 3 });

        report.Outcome.ShouldBe(CounterfactualOutcome.Inconclusive);
        report.Reasons.Single().ShouldContain("judged windows");
    }

    [Fact]
    public void A_short_history_is_inconclusive_and_says_why()
    {
        var report = Run(Regime(60));

        report.Outcome.ShouldBe(CounterfactualOutcome.Inconclusive);
        report.Windows.ShouldBeEmpty();
        report.Reasons.Single().ShouldContain("too short");
    }

    [Fact]
    public void The_period_is_shortened_to_what_the_history_allows_and_the_report_says_so()
    {
        var report = Run(Regime(270));

        report.FrozenAgeMonths.ShouldBeLessThan(12);
        report.Limits.ShouldContain(l => l.Contains("what this data allow", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_rows_in_any_order_give_the_same_text()
    {
        var rows = Regime(340);

        var a = CounterfactualEvaluator.Evaluate(rows, new Recent(), Options, Folds).Value.ToText();
        var b = CounterfactualEvaluator.Evaluate([.. rows.AsEnumerable().Reverse()], new Recent(), Options, Folds).Value.ToText();

        a.ShouldBe(b);
        a.ShouldContain("limit:");
    }

    [Fact]
    public void A_margin_above_the_gain_gives_no_difference()
    {
        var strict = Options with { Trials = 1, MinGainLogLoss = 5 };

        var report = Run(Regime(340), o: strict);

        report.Outcome.ShouldBe(CounterfactualOutcome.NoDifference);
    }

    [Fact]
    public void A_row_in_the_hold_out_is_refused()
    {
        var rows = Regime(340);

        var result = CounterfactualEvaluator.Evaluate(rows, new Recent(), Options, Folds with { HoldOutStart = new DateOnly(2025, 6, 1) });

        result.Error.Code.ShouldBe("validation.holdout_touched");
    }

    [Fact]
    public void Bad_input_is_a_failure_with_a_code()
    {
        var rows = Regime(340);
        var duplicate = new List<ValidationRow>(rows) { rows[0] };

        CounterfactualEvaluator.Evaluate([], new Recent(), Options, Folds).Error.Code.ShouldBe("validation.no_rows");
        CounterfactualEvaluator.Evaluate(duplicate, new Recent(), Options, Folds).Error.Code.ShouldBe("validation.duplicate_key");
        CounterfactualEvaluator.Evaluate(rows, new Recent(), Options with { FrozenMonths = 24 }, Folds).Error.Code.ShouldBe("counterfactual.options");
    }

    [Fact]
    public void A_probability_outside_zero_to_one_is_a_failure()
    {
        var result = CounterfactualEvaluator.Evaluate(Regime(340), new Wild(), Options, Folds);

        result.Error.Code.ShouldBe("validation.bad_probability");
    }

    private sealed class Wild : IScorerFactory
    {
        public string Name => "wild";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Bad();

        private sealed class Bad : IRowScorer
        {
            public double Score(ValidationRow row) => 1.5;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Frozen_months_outside_one_to_twelve_are_refused(int months)
    {
        new CounterfactualOptions(FrozenMonths: months).Problems().ShouldNotBeEmpty();
    }

    /// <summary>Retraining predicts the labels well (better log loss) while the frozen side is a constant that takes nothing; the realised R of the labelled winners is set by the caller.</summary>
    private sealed class Oracle : IScorerFactory
    {
        private int? _first;

        public string Name => "oracle";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train)
        {
            _first ??= train.Count;
            return train.Count <= _first ? new Scorer(false) : new Scorer(true);
        }

        private sealed class Scorer(bool good) : IRowScorer
        {
            public double Score(ValidationRow row) => good ? (row.Features[0] > 0.5 ? 0.9 : 0.1) : 0.3;
        }
    }

    private static List<ValidationRow> WinnersPay(double r) =>
        Regime(340, flip: new DateOnly(2100, 1, 1)).Select(x => x with { R = x.Label == 1 ? r : -r }).ToList();

    [Fact]
    public void A_better_log_loss_with_a_worse_realised_r_is_capped_at_inconclusive()
    {
        var report = Run(WinnersPay(-1), new Oracle());

        report.Judged!.MeanGain.ShouldBeGreaterThan(0.1);
        report.Judged.Retrained.Policy.PolicyPerCandidate.ShouldBeLessThan(report.Judged.Frozen.Policy.PolicyPerCandidate);
        report.Outcome.ShouldBe(CounterfactualOutcome.Inconclusive);
        report.Reasons.Single().ShouldContain("disagree");
    }

    [Fact]
    public void A_negligible_realised_r_gap_does_not_cap_the_outcome()
    {
        var report = Run(WinnersPay(-0.001), new Oracle());

        report.Outcome.ShouldBe(CounterfactualOutcome.RetrainingHelps);
    }

    [Fact]
    public void A_verdict_on_fewer_than_six_judged_windows_is_labelled_weak()
    {
        var report = Run(Regime(340));

        report.Outcome.ShouldBe(CounterfactualOutcome.RetrainingHelps);
        report.Reasons.Single().ShouldContain("weak (3 windows)");
        report.Reasons.Single().ShouldContain("clustered");
    }

    [Fact]
    public void Trades_of_the_same_day_move_together_so_the_clustered_error_exceeds_the_independent_one()
    {
        var start = new DateOnly(2025, 1, 1);
        var gains = Enumerable.Range(0, 20).SelectMany(d => Enumerable.Repeat((start.AddDays(d), d % 2 == 0 ? 0.3 : -0.1), 10)).ToList();
        var mean = gains.Average(g => g.Item2);
        var independent = Math.Sqrt(gains.Sum(g => (g.Item2 - mean) * (g.Item2 - mean)) / (gains.Count - 1) / gains.Count);

        var clustered = CounterfactualEvaluator.ClusteredStandardError(gains).ShouldNotBeNull();

        clustered.ShouldBeGreaterThan(independent * 2.5);
        CounterfactualEvaluator.ClusteredStandardError(gains.Take(10)).ShouldBeNull();
    }

    [Fact]
    public void Only_a_window_of_full_length_counts()
    {
        var report = Run(Regime(340));

        report.Windows.ShouldAllBe(w => w.End == w.Start.AddMonths(3).AddDays(-1));
        report.Limits.ShouldContain(l => l.Contains("after the last window", StringComparison.Ordinal));
    }

    [Fact]
    public void The_limits_of_a_short_history_say_from_the_first_to_the_last_day()
    {
        var rows = Regime(60);

        var report = Run(rows);

        report.Limits.ShouldContain(l => l.Contains($"from {rows.Min(r => r.TradeDate):yyyy-MM-dd} to {rows.Max(r => r.TradeDate):yyyy-MM-dd}", StringComparison.Ordinal));
    }

    [Fact]
    public void A_train_set_of_one_class_is_inconclusive_without_fitting()
    {
        var log = new List<Fit>();
        var rows = Enumerable.Range(0, 340).Select(i => Make(i, true, Feature(i))).ToList();

        var report = Run(rows, new Recent(log));

        report.Outcome.ShouldBe(CounterfactualOutcome.Inconclusive);
        report.Reasons.Single().ShouldContain("one class");
        log.ShouldBeEmpty();
    }

    [Fact]
    public void The_text_prints_the_period_the_windows_and_the_trials()
    {
        var text = Run(Regime(340)).ToText();

        text.ShouldContain("frozen at 2024-11-01");
        text.ShouldContain("window 1 ");
        text.ShouldContain("same model on both sides, not judged");
        text.ShouldContain(string.Create(CultureInfo.InvariantCulture, $"trials {1}"));
    }
}
