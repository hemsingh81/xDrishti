using System.Globalization;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The ladder reads the paired day-cluster bootstrap and shows the fold sign test beside it; it reports only (CF-10 part 10c-1, ADR 0216).</summary>
public sealed class WalkForwardPairedTests
{
    private static readonly DateOnly Start = new(2024, 1, 2);
    private static readonly LadderOptions Ladder = new(MinRows: 5, MinTreeEvents: 10);
    private static readonly PairedBootstrapOptions Paired = new();

    /// <summary>Scores by the size of the first feature: high when its absolute value is large (the shape a straight line cannot fit).</summary>
    private sealed class AbsoluteSize : IScorerFactory
    {
        public string Name => "abs";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Model();

        private sealed class Model : IRowScorer
        {
            public double Score(ValidationRow row) => Math.Abs(row.Features[0]) > 3 ? 0.95 : 0.05;
        }
    }

    private sealed class Constant : IScorerFactory
    {
        public string Name => "constant";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Fixed();

        private sealed class Fixed : IRowScorer
        {
            public double Score(ValidationRow row) => 0.5;
        }
    }

    private static List<ValidationRow> Rows(Func<int, int, double> feature) =>
        [.. Enumerable.Range(0, 240).Select(i =>
        {
            var date = Start.AddDays(i * 3);
            var epoch = new DateTimeOffset(date.Year, date.Month, date.Day, 4, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            var label = i % 3 == 0 ? 1 : 0;
            return new ValidationRow("k" + i.ToString("D4", CultureInfo.InvariantCulture), label, label == 1 ? 1.5 : -1, date, epoch, epoch + 3600, i % 2 == 0 ? "A" : "B", [feature(i, label)]);
        })];

    // a straight line fits this one: the label is the size of the feature
    private static List<ValidationRow> Linear() => Rows((i, label) => (label * 4) + (((i % 5) - 2) * 0.1));

    // a straight line cannot fit this one: the label is the size of the feature, whichever sign it has
    private static List<ValidationRow> Absolute() => Rows((i, label) => ((i % 2 == 0 ? 1 : -1) * ((label * 4.5) + 1)) + (((i % 5) - 2) * 0.1));

    [Fact]
    public void A_logistic_model_that_fits_the_data_earns_its_rung_on_the_probability_and_the_cap()
    {
        var report = WalkForwardValidator.Compare(Linear(), [new Constant()], new ValidationOptions(Ladder: Ladder, Paired: Paired)).Value;
        var c = report.Complexity!;
        var logistic = c.Ladder.Single(a => a.Scorer == "logistic");

        logistic.ProbabilityBeatsBelow.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(0.95);
        logistic.Earned.ShouldBeTrue();
        logistic.Reasons.ShouldBeEmpty();
        var paired = c.Paired.ShouldNotBeNull();
        paired.Select(p => (p.Scorer, p.Below)).ShouldBe([("logistic", "cell_rate"), ("constant", "logistic")]);
        paired[0].Reading.ShouldBe(PairedReading.Pass);
        paired[0].Sign.Folds.ShouldBe(report.Results.Count);
    }

    [Fact]
    public void A_candidate_that_beats_the_logistic_baseline_with_certainty_still_cannot_read_earned_the_trainer_is_not_rung_4()
    {
        var report = WalkForwardValidator.Compare(Absolute(), [new AbsoluteSize()], new ValidationOptions(Ladder: Ladder, Paired: Paired)).Value;
        var abs = report.Complexity!.Ladder.Single(a => a.Scorer == "abs");

        abs.ProbabilityBeatsBelow.ShouldBe(1.0);
        abs.Earned.ShouldBeFalse();
        abs.Reasons.ShouldBe([ComplexityEvidence_TreesNotRung4()]);
        report.Complexity.Paired!.Single(p => p.Scorer == "abs").Reading.ShouldBe(PairedReading.Pass);
        report.ToText().ShouldContain("rung 4 Trees, 1 features: not earned - the trainer has 8 leaves");
    }

    [Fact]
    public void The_report_prints_the_probability_the_interval_the_sign_test_and_the_effective_count()
    {
        var report = WalkForwardValidator.Compare(Linear(), [new Constant()], new ValidationOptions(Ladder: Ladder, Paired: Paired)).Value;
        var text = report.ToText();
        var folds = report.Results.Count;

        text.ShouldContain("  paired logistic against cell_rate: P(better) ");
        text.ShouldContain("90% interval [");
        text.ShouldContain($"folds better {folds} of {folds}, sign test p {FoldSignTest.PValue(folds, folds)!.Value.ToString("F4", CultureInfo.InvariantCulture)}");
        text.ShouldContain("  paired constant against logistic: ");
        text.ShouldContain("effective count of the last training set (overlapping labels counted once): ");
        text.ShouldContain("; paired ReportOnly/draws=2000/level=0.9/minDays=10/promising=0.9");
        WalkForwardValidator.Compare(Linear(), [new Constant()], new ValidationOptions(Ladder: Ladder, Paired: Paired)).Value.ToText().ShouldBe(text);
    }

    [Fact]
    public void Labels_that_overlap_make_the_effective_count_smaller_than_the_rows()
    {
        var rows = Linear().Select(r => r with { ResolvedEpochSeconds = r.DecisionEpochSeconds + (86400L * 5) }).ToList(); // each label spans 5 days, rows are 3 days apart
        var report = WalkForwardValidator.Compare(rows, [new Constant()], new ValidationOptions(Ladder: Ladder)).Value;

        report.Complexity!.EffectiveRows.ShouldNotBeNull().ShouldBeLessThan(report.Complexity.TrainRows);
        var apart = WalkForwardValidator.Compare(Linear(), [new Constant()], new ValidationOptions(Ladder: Ladder)).Value.Complexity!;

        apart.EffectiveRows.ShouldBe(apart.TrainRows); // labels of an hour, three days apart, do not overlap
    }

    [Fact]
    public void With_the_paired_bootstrap_off_or_not_given_the_probability_stays_not_available()
    {
        var none = WalkForwardValidator.Compare(Linear(), [new Constant()], new ValidationOptions(Ladder: Ladder)).Value;
        var off = WalkForwardValidator.Compare(Linear(), [new Constant()], new ValidationOptions(Ladder: Ladder, Paired: new PairedBootstrapOptions(ConfidenceMode.Off))).Value;

        off.ToText().ShouldBe(none.ToText());
        none.Complexity!.Paired.ShouldBeEmpty();
        none.Complexity.Ladder.ShouldAllBe(a => a.ProbabilityBeatsBelow == null && !a.Earned);
        none.ToText().ShouldNotContain("  paired ");
    }

    [Fact]
    public void The_paired_bootstrap_does_nothing_when_the_ladder_is_off()
    {
        var report = WalkForwardValidator.Compare(Linear(), [new Constant()], new ValidationOptions(Paired: Paired)).Value;

        report.Complexity.ShouldBeNull();
    }

    [Fact]
    public void Too_few_trade_days_leaves_the_probability_not_available_and_still_shows_the_sign_test()
    {
        var report = WalkForwardValidator.Compare(Linear(), [new Constant()], new ValidationOptions(Ladder: Ladder, Paired: new PairedBootstrapOptions(MinDays: 1000))).Value;
        var logistic = report.Complexity!.Ladder.Single(a => a.Scorer == "logistic");
        var pair = report.Complexity.Paired![0];

        pair.Bootstrap.ShouldBeNull();
        pair.Unavailable.ShouldBe(PairedDayBootstrap.TooFewDaysCode);
        pair.Reading.ShouldBe(PairedReading.Below);
        logistic.ProbabilityBeatsBelow.ShouldBeNull();
        logistic.Earned.ShouldBeFalse(); // an unavailable figure is never a pass
        report.ToText().ShouldContain("paired logistic against cell_rate: not available (bootstrap.too_few_days); folds better ");
    }

    [Fact]
    public void Nothing_the_paired_bootstrap_adds_changes_a_verdict_or_a_metric()
    {
        var without = WalkForwardValidator.Compare(Linear(), [new Constant()], new ValidationOptions(Ladder: Ladder)).Value;
        var with = WalkForwardValidator.Compare(Linear(), [new Constant()], new ValidationOptions(Ladder: Ladder, Paired: Paired)).Value;

        with.Verdicts.Select(v => (v.Outcome, v.FoldsBeatingBoth, string.Join('|', v.Reasons))).ShouldBe(without.Verdicts.Select(v => (v.Outcome, v.FoldsBeatingBoth, string.Join('|', v.Reasons))));
        with.Pooled.Select(p => p.Metrics).ShouldBe(without.Pooled.Select(p => p.Metrics));
    }

    [Fact]
    public void A_promising_level_above_the_pass_bar_is_a_validation_problem()
    {
        new ValidationOptions(Ladder: new LadderOptions(MinProbability: 0.85), Paired: new PairedBootstrapOptions()).Problems().ShouldNotBeEmpty();
        new ValidationOptions(Ladder: new LadderOptions(), Paired: new PairedBootstrapOptions(Draws: 50)).Problems().ShouldNotBeEmpty();
        new ValidationOptions(Ladder: new LadderOptions(), Paired: new PairedBootstrapOptions()).Problems().ShouldBeEmpty();
        WalkForwardValidator.Compare(Linear(), [new Constant()], new ValidationOptions(Ladder: Ladder, Paired: new PairedBootstrapOptions(Level: 0.4))).IsFailure.ShouldBeTrue();
    }

    private static string ComplexityEvidence_TreesNotRung4() =>
        "the trainer has 8 leaves per tree and no monotone constraints, which the trees rung of the ladder requires (monotone, depth at most 3)";
}
