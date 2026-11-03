namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>
/// P15-08 criterion 7: set D passes only with at least 200 rows, at least 30 predicted material, precision of material at least 0.80 (exact), and a model that beats the category baseline by the exact sign test; it prints the baseline's figures, b, c, p, the weak-evidence label
/// and the weighted precision.
/// </summary>
public sealed class AnnouncementGraderTests
{
    private static readonly ThresholdTable Table = EvalTables.Thresholds();

    /// <summary>Rows with the model's confusion counts; the baseline calls nothing material (the weakest baseline), unless <paramref name="baseline"/> says otherwise.</summary>
    private static List<AnnouncementRow> Rows(int tp, int fp, int fn, int tn, Func<AnnouncementRow, bool?>? baseline = null)
    {
        var rows = new List<AnnouncementRow>();
        rows.AddRange(Enumerable.Range(0, tp).Select(i => new AnnouncementRow($"tp{i}", true, true)));
        rows.AddRange(Enumerable.Range(0, fp).Select(i => new AnnouncementRow($"fp{i}", false, true)));
        rows.AddRange(Enumerable.Range(0, fn).Select(i => new AnnouncementRow($"fn{i}", true, false)));
        rows.AddRange(Enumerable.Range(0, tn).Select(i => new AnnouncementRow($"tn{i}", false, false)));
        return [.. rows.Select(r => r with { BaselineMaterial = baseline is null ? false : baseline(r) })];
    }

    [Fact]
    public void The_thresholds_file_holds_the_two_new_keys()
    {
        Table.EventsMinPredictedMaterial.ShouldBe(30);
        Table.EventsBaselineSignP.ShouldBe(0.05m);
    }

    [Fact]
    public void Thirty_six_of_forty_predicted_passes_and_is_labelled_weak_evidence_with_the_wilson_interval_of_the_plan()
    {
        var result = AnnouncementGrader.Grade(Rows(36, 4, 14, 146), Table);

        result.State.ShouldBe(SetState.Pass);
        result.Precision.ShouldNotBeNull().ShouldBe(0.9, 1e-12);
        result.PrecisionInterval!.Value.Low.ShouldBe(0.770, 0.001);
        result.PrecisionInterval.Value.High.ShouldBe(0.960, 0.001);
        result.Detail.ShouldNotBeNull().WeakEvidence.ShouldBeTrue();
        result.Detail.Lines().ShouldContain(l => l.StartsWith("weak evidence", StringComparison.Ordinal));
        result.Note.ShouldBeNull();
    }

    [Fact]
    public void Twenty_five_of_twenty_eight_predicted_fails_with_too_few_predicted()
    {
        var result = AnnouncementGrader.Grade(Rows(25, 3, 20, 152), Table);

        result.State.ShouldBe(SetState.Fail);
        result.Note.ShouldNotBeNull().ShouldContain("too_few_predicted: 28 predicted material, 30 needed");
        result.Note.ShouldNotContain("precision of material is below");
    }

    [Fact]
    public void A_hundred_and_sixty_of_two_hundred_passes_and_a_hundred_and_fifty_nine_fails()
    {
        var pass = AnnouncementGrader.Grade(Rows(160, 40, 0, 0), Table);
        var fail = AnnouncementGrader.Grade(Rows(159, 41, 0, 0), Table);

        pass.State.ShouldBe(SetState.Pass);
        pass.Precision!.Value.ShouldBe(0.80, 1e-12);
        pass.Detail!.WeakEvidence.ShouldBeTrue();
        fail.State.ShouldBe(SetState.Fail);
        fail.Note.ShouldBe("precision of material is below the threshold");
    }

    [Fact]
    public void A_model_equal_to_the_baseline_fails_even_with_perfect_precision()
    {
        var rows = Rows(40, 0, 0, 160).Select(r => r with { BaselineMaterial = r.PredictedMaterial }).ToList();

        var result = AnnouncementGrader.Grade(rows, Table);

        result.State.ShouldBe(SetState.Fail);
        result.Note.ShouldNotBeNull().ShouldContain("baseline_not_beaten");
        (result.Detail!.B, result.Detail.C, result.Detail.PValue).ShouldBe((0, 0, 1.0));
    }

    [Fact]
    public void A_model_worse_than_the_baseline_fails()
    {
        // The baseline calls every labelled-material row material; the model misses 20 of them.
        var rows = Rows(60, 0, 20, 120, r => r.LabelMaterial);

        var result = AnnouncementGrader.Grade(rows, Table);

        result.State.ShouldBe(SetState.Fail);
        result.Detail!.B.ShouldBe(20);
        result.Detail.C.ShouldBe(0);
        result.Note.ShouldNotBeNull().ShouldContain("baseline_not_beaten");
    }

    [Fact]
    public void More_wins_than_losses_that_the_sign_test_cannot_separate_from_chance_still_fails()
    {
        // Model right and baseline wrong on 6 rows (tp0 to tp5), the reverse on 2 (fn0, fn1); the baseline copies the model everywhere else: c > b but the exact two-sided p is 0.289.
        var rows = Rows(40, 0, 2, 158, r => r.Id switch
        {
            "tp0" or "tp1" or "tp2" or "tp3" or "tp4" or "tp5" => false,
            "fn0" or "fn1" => true,
            _ => r.PredictedMaterial,
        });

        var result = AnnouncementGrader.Grade(rows, Table);

        (result.Detail!.B, result.Detail.C).ShouldBe((2, 6));
        result.Detail.PValue.ShouldBe(0.2890625, 1e-9);
        result.State.ShouldBe(SetState.Fail);
        result.Note.ShouldNotBeNull().ShouldContain("baseline_not_beaten");
    }

    [Fact]
    public void A_row_without_a_baseline_class_cannot_pass()
    {
        var rows = Rows(40, 0, 0, 160).Select(r => r with { BaselineMaterial = null }).ToList();

        var result = AnnouncementGrader.Grade(rows, Table);

        result.State.ShouldBe(SetState.Fail);
        result.Note.ShouldNotBeNull().ShouldContain("baseline_missing");
    }

    [Fact]
    public void Fewer_than_200_rows_are_not_evaluable_whatever_the_precision()
    {
        var result = AnnouncementGrader.Grade(Rows(36, 4, 0, 159), Table);

        result.State.ShouldBe(SetState.NotEvaluable);
        result.Detail.ShouldBeNull();
    }

    [Fact]
    public void The_detail_prints_the_baselines_counts_the_unstable_cases_and_the_weighted_precision_from_the_weights()
    {
        var rows = Rows(36, 4, 14, 146).Select(r => r with { Weight = r.Id.StartsWith("fp", StringComparison.Ordinal) ? 10.0 : 1.0, Unstable = r.Id is "tn0" or "tn1" }).ToList();

        var result = AnnouncementGrader.Grade(rows, Table);

        // Weighted precision: 36 * 1 / (36 * 1 + 4 * 10) = 0.4737.
        result.Detail!.WeightedPrecision.ShouldNotBeNull().ShouldBe(36.0 / 76, 1e-12);
        result.Detail.Unstable.ShouldBe(2);
        (result.Detail.BaselineTruePositive, result.Detail.BaselineFalsePositive, result.Detail.BaselineFalseNegative).ShouldBe((0, 0, 50));
        var lines = result.Detail.Lines();
        lines.ShouldContain(l => l.Contains("0.474", StringComparison.Ordinal));
        lines.ShouldContain("unstable: 2 cases changed their answer between repeats and count as not material");
        lines[0].ShouldContain("b 4");
    }

    [Fact]
    public void Without_weights_the_weighted_precision_is_not_available()
    {
        var result = AnnouncementGrader.Grade(Rows(36, 4, 14, 146), Table);

        result.Detail!.WeightedPrecision.ShouldBeNull();
        result.Detail.Lines().ShouldContain(l => l.Contains("n/a", StringComparison.Ordinal));
    }

    [Fact]
    public void The_summary_lists_the_baseline_rates_and_the_detail_lines_under_the_set()
    {
        var result = AnnouncementGrader.Grade(Rows(36, 4, 14, 146, r => r.LabelMaterial && r.Id.StartsWith("tp", StringComparison.Ordinal) && r.Id.Length % 2 == 0), Table);

        var summary = EvalSummarizer.Build([], Table, 3, result);

        var set = summary.Sets.Single();
        set.Set.ShouldBe("events");
        set.Rates.Select(r => r.Name).ShouldContain("precision_material");
        set.Rates.Select(r => r.Name).ShouldContain("baseline_precision_material");
        set.Outcome.Reasons.ShouldContain(r => r.StartsWith("paired with the category baseline", StringComparison.Ordinal));
        set.Repeats.ShouldBe(3);
    }
}
