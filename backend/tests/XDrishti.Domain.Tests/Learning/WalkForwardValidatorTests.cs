using System.Collections.Immutable;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The folds, the purge, the no-leak and determinism properties, the honest outcomes and the hold-out (P6-05, ADR 0067).</summary>
public sealed class WalkForwardValidatorTests
{
    private static readonly DateOnly Start = new(2024, 1, 2);

    private static readonly ValidationOptions Defaults = new();

    private static long Epoch(DateOnly d) => new DateTimeOffset(d.Year, d.Month, d.Day, 4, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static ValidationRow Make(int i, DateOnly date, int label, long hold = 3600) =>
        new("k" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), label, label == 1 ? 1.5 : -1, date, Epoch(date), Epoch(date) + hold, i % 2 == 0 ? "A" : "B", [label + ((i % 7) * 0.1), i % 3]);

    /// <summary>A trade every 3 days for 720 days (240 rows); label follows feature 0 only through the cell, so the cell rate has signal.</summary>
    private static List<ValidationRow> Data(Func<int, int>? label = null) =>
        Enumerable.Range(0, 240).Select(i => Make(i, Start.AddDays(i * 3), (label ?? (j => j % 2 == 0 ? (j % 10 == 0 ? 0 : 1) : (j % 6 == 1 ? 1 : 0)))(i))).ToList();

    private static ValidationReport Run(IReadOnlyList<ValidationRow> rows, IReadOnlyList<IScorerFactory>? candidates = null, ValidationOptions? o = null) =>
        WalkForwardValidator.Compare(rows, candidates ?? [], o ?? Defaults).Value;

    private sealed class Cheat : IScorerFactory
    {
        public string Name => "cheat";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Perfect();

        private sealed class Perfect : IRowScorer
        {
            public double Score(ValidationRow row) => row.Label;
        }
    }

    private sealed class Constant(string name, double p) : IScorerFactory
    {
        public string Name => name;

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Fixed(p);

        private sealed class Fixed(double p) : IRowScorer
        {
            public double Score(ValidationRow row) => p;
        }
    }

    private sealed class Recorder : IScorerFactory
    {
        public List<IReadOnlyList<ValidationRow>> Trains { get; } = [];

        public string Name => "recorder";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train)
        {
            Trains.Add(train.ToArray());
            return new Constant("x", 0.5).Fit(train);
        }
    }

    [Fact]
    public void Quarterly_folds_start_nine_months_in_and_expand()
    {
        var report = Run(Data());

        report.Folds.Count.ShouldBe(5);
        report.Folds[0].TestStart.ShouldBe(new DateOnly(2024, 11, 1));
        report.Folds[0].TestEnd.ShouldBe(new DateOnly(2025, 1, 31));
        report.Folds.Select(f => f.TrainCount).ShouldBe(report.Folds.Select(f => f.TrainCount).Order());
        report.Results.Count.ShouldBe(5);
        report.Results[0].Scorers.Select(s => s.Name).ShouldBe(["base_rate", "cell_rate", "logistic"]);
    }

    [Fact]
    public void A_train_row_whose_horizon_reaches_the_test_window_is_purged_and_the_embargo_drops_the_week_before()
    {
        var rows = Data();
        rows[0] = Make(0, new DateOnly(2024, 10, 20), 1, hold: 20 * 86400); // resolves after the first decision of the window
        rows.Add(Make(900, new DateOnly(2024, 10, 28), 1)); // inside the 7 day embargo
        var recorder = new Recorder();

        var report = Run(rows, [recorder]);

        report.Folds[0].Purged.ShouldBe(1);
        report.Folds[0].Embargoed.ShouldBeGreaterThanOrEqualTo(1);
        recorder.Trains[0].ShouldNotContain(r => r.Key == "k0000");
        recorder.Trains[0].ShouldNotContain(r => r.Key == "k0900");
    }

    [Fact]
    public void A_scorer_sees_only_rows_that_are_over_before_the_test_window_begins()
    {
        var recorder = new Recorder();

        var report = Run(Data(), [recorder]);

        for (var f = 0; f < report.Results.Count; f++)
        {
            var fold = report.Results[f].Fold;
            var first = Data().Where(r => r.TradeDate >= fold.TestStart && r.TradeDate <= fold.TestEnd).Min(r => r.DecisionEpochSeconds);
            recorder.Trains[f].ShouldAllBe(r => r.TradeDate < fold.TestStart && r.ResolvedEpochSeconds + 60 < first);
            recorder.Trains[f].Count.ShouldBe(fold.TrainCount);
        }
    }

    [Fact]
    public void Changing_a_later_fold_or_dropping_later_rows_does_not_change_an_earlier_fold()
    {
        var full = Run(Data(), [new Cheat()]);
        var changed = Data(i => i >= 200 ? 1 - (i % 2) : (i % 2));
        var changedReport = Run(Data().Select((r, i) => i >= 200 ? r with { Label = 1 - r.Label } : r).ToList(), [new Cheat()]);
        var truncated = Run(Data().Where(r => r.TradeDate <= full.Results[1].Fold.TestEnd).ToList(), [new Cheat()]);

        changed.Count.ShouldBe(240);
        for (var i = 0; i < 3; i++)
        {
            changedReport.Results[i].Scorers.ShouldBe(full.Results[i].Scorers);
        }

        truncated.Results[0].Scorers.ShouldBe(full.Results[0].Scorers);
        truncated.Results[1].Scorers.ShouldBe(full.Results[1].Scorers);
    }

    [Fact]
    public void The_same_rows_in_any_order_give_the_same_report_text()
    {
        var rows = Data();
        var shuffled = rows.OrderBy(r => r.Key.GetHashCode(StringComparison.Ordinal) % 97).ThenBy(r => r.Key, StringComparer.Ordinal).ToList();

        var a = Run(rows, [new Cheat()]).ToText();
        var b = Run(rows, [new Cheat()]).ToText();
        var c = Run(shuffled, [new Cheat()]).ToText();

        b.ShouldBe(a);
        c.ShouldBe(a);
    }

    [Fact]
    public void A_scorer_with_no_skill_cannot_beat_the_baselines()
    {
        var report = Run(Data(), [new Constant("flat", 0.5)]);

        var v = report.Verdicts.ShouldHaveSingleItem();
        v.Outcome.ShouldBe(ValidationOutcome.CannotBeatBaselines);
        v.Reasons.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_perfect_scorer_is_inconclusive_on_five_folds_and_eligible_when_five_are_enough()
    {
        Run(Data(), [new Cheat()]).Verdicts[0].Outcome.ShouldBe(ValidationOutcome.Inconclusive);
        Run(Data(), [new Cheat()], new ValidationOptions(MinFoldsForVerdict: 5)).Verdicts[0].Outcome.ShouldBe(ValidationOutcome.Eligible);
    }

    [Fact]
    public void The_report_carries_the_always_take_baseline_and_the_honest_limits()
    {
        var report = Run(Data());

        report.Results[0].AlwaysTake.N.ShouldBe(report.Results[0].Fold.TestCount);
        report.Limits.ShouldContain(l => l.Contains("modelled fills"));
        report.Limits.ShouldContain(l => l.Contains("Fewer than 6 folds"));
        report.Limits.ShouldContain(l => l.Contains("No hold-out"));
    }

    [Fact]
    public void Too_little_history_is_a_failure_not_a_verdict()
    {
        var rows = Data().Take(60).ToList();

        var result = WalkForwardValidator.Compare(rows, [new Cheat()], Defaults);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("validation.no_folds");
    }

    [Fact]
    public void Folds_with_too_few_train_rows_are_not_used_and_leave_no_verdict()
    {
        var result = WalkForwardValidator.Compare(Data(), [new Cheat()], new ValidationOptions(MinTrainTrades: 1000));

        result.Error.Code.ShouldBe("validation.no_folds");
    }

    [Theory]
    [InlineData("duplicate", "validation.duplicate_key")]
    [InlineData("label", "validation.bad_row")]
    [InlineData("width", "validation.feature_width")]
    [InlineData("names", "validation.scorer_names")]
    [InlineData("options", "validation.options")]
    [InlineData("empty", "validation.no_rows")]
    public void Bad_input_is_refused_with_a_code(string what, string code)
    {
        var rows = Data();
        var candidates = new List<IScorerFactory>();
        var options = Defaults;
        switch (what)
        {
            case "duplicate": rows[1] = rows[0]; break;
            case "label": rows[0] = rows[0] with { Label = 2 }; break;
            case "width": rows[3] = rows[3] with { Features = ImmutableArray.Create(1.0) }; break;
            case "names": candidates.Add(new Constant("base_rate", 0.5)); break;
            case "options": options = new ValidationOptions(TestMonths: 0); break;
            default: rows.Clear(); break;
        }

        var result = WalkForwardValidator.Compare(rows, candidates, options);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
    }

    [Fact]
    public void A_probability_outside_zero_to_one_is_refused()
    {
        var result = WalkForwardValidator.Compare(Data(), [new Constant("bad", 1.5)], Defaults);

        result.Error.Code.ShouldBe("validation.bad_probability");
    }

    [Fact]
    public void The_hold_out_is_the_last_six_months_and_development_may_not_read_it()
    {
        var rows = Data();
        rows[170] = Make(170, rows[170].TradeDate, 1, hold: 40 * 86400); // a trade whose horizon runs into the hold-out

        var split = HoldOut.Reserve(rows).Value;

        split.Start.ShouldBe(rows.Max(r => r.TradeDate).AddMonths(-6).AddDays(1));
        split.Development.ShouldAllBe(r => r.TradeDate < split.Start);
        split.Reserved.ShouldAllBe(r => r.TradeDate >= split.Start);
        split.Purged.ShouldBeGreaterThanOrEqualTo(1);
        (split.Development.Count + split.Purged + split.Reserved.Count).ShouldBe(rows.Count);

        var ok = WalkForwardValidator.Compare(split.Development, [], Defaults with { HoldOutStart = split.Start, MinTrainMonths = 6 });
        ok.IsSuccess.ShouldBeTrue();
        ok.Value.Limits.ShouldContain(l => l.Contains("was not read"));

        var touched = WalkForwardValidator.Compare(rows, [], Defaults with { HoldOutStart = split.Start });
        touched.Error.Code.ShouldBe("validation.holdout_touched");
    }

    [Fact]
    public void An_empty_hold_out_or_development_is_refused()
    {
        HoldOut.Reserve([]).Error.Code.ShouldBe("validation.no_rows");
        HoldOut.Reserve(Data().Take(10).ToList(), months: 12).Error.Code.ShouldBe("validation.holdout_empty");
        HoldOut.Reserve(Data(), months: 0).Error.Code.ShouldBe("validation.holdout_months");
    }

    [Fact]
    public void Rows_are_made_from_a_training_set_context_and_a_missing_one_is_refused()
    {
        var label = new XDrishti.Domain.Research.TripleBarrierLabel("k1", 1, XDrishti.Domain.Research.BarrierOutcome.Target, XDrishti.Domain.Research.LabelBasis.Net, 1.5, 10, 0, 100, 700, false);
        var set = new XDrishti.Domain.Research.TrainingSet("id", "tb-1", ["r"], XDrishti.Domain.Research.LabelBasis.Net, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1), 1, 0, 0, 0, [label]);

        var ok = ValidationRows.From(set, new Dictionary<string, ValidationContext> { ["k1"] = new(new DateOnly(2025, 1, 1), "A", [1.0]) });
        var missing = ValidationRows.From(set, new Dictionary<string, ValidationContext>());

        ok.Value.ShouldHaveSingleItem().ResolvedEpochSeconds.ShouldBe(700);
        missing.Error.Code.ShouldBe("validation.no_context");
    }
}
