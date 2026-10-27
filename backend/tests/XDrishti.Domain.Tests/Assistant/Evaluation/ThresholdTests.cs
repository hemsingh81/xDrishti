namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>Spec P9-09 AC 7 and 9: thresholds are read from the file; the verdict; the events grader on a synthetic 200-row table.</summary>
public sealed class ThresholdTests
{
    private static readonly ThresholdTable Table = EvalTables.Thresholds();

    [Fact]
    public void The_file_holds_the_thresholds_of_doc_14()
    {
        Table.ShouldBe(new ThresholdTable(30, 27, 10, 8, 9, 0, true, 10, 8, 9, 0.85, 0.60, 2, 0.80m, 200));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""{"data":{"of":30,"pass_min":31},"plans":{"of":10,"raw_pass_min":8,"repaired_pass_min":9,"escape_max":0,"covered_all":true},"ideas":{"of":10,"first_shot_valid_min":8,"repaired_valid_min":9,"intent_min":0.85,"intent_floor":0.6,"decline_correct_min":2},"events":{"precision_material_min":0.8,"min_rows":200}}""")]
    public void A_file_that_is_not_a_threshold_table_is_refused(string json) =>
        ThresholdTable.Parse(json).Error.Code.ShouldBe(ThresholdTable.InvalidCode);

    [Theory]
    [InlineData(30, 30, SetState.Pass)]
    [InlineData(27, 30, SetState.Pass)]
    [InlineData(26, 30, SetState.Fail)]
    [InlineData(30, 29, SetState.NotRun)]
    public void Data_passes_at_twenty_seven_of_thirty_only_when_all_thirty_ran(int passes, int cases, SetState expected) =>
        Table.EvaluateData(passes, cases).State.ShouldBe(expected);

    [Fact]
    public void Plans_need_raw_repaired_no_escape_and_every_symbol_covered()
    {
        Table.EvaluatePlans(new PlanCounts(10, 8, 9, 0, 10)).State.ShouldBe(SetState.Pass);
        Table.EvaluatePlans(new PlanCounts(10, 7, 9, 0, 10)).State.ShouldBe(SetState.Fail);
        Table.EvaluatePlans(new PlanCounts(10, 8, 8, 0, 10)).State.ShouldBe(SetState.Fail);
        Table.EvaluatePlans(new PlanCounts(10, 10, 10, 1, 10)).Reasons.ShouldBe(["escape 1 is above 0"]);
        Table.EvaluatePlans(new PlanCounts(10, 10, 10, 0, 9)).Reasons.ShouldBe(["covered 9 of 10"]);
        Table.EvaluatePlans(new PlanCounts(9, 9, 9, 0, 9)).State.ShouldBe(SetState.NotRun);
    }

    [Fact]
    public void Ideas_need_first_shot_repaired_intent_the_floor_and_both_declines()
    {
        Table.EvaluateIdeas(new IdeaCounts(10, 8, 9, 0.85, 0.60, 2, 2)).State.ShouldBe(SetState.Pass);
        Table.EvaluateIdeas(new IdeaCounts(10, 7, 9, 0.9, 0.7, 2, 2)).State.ShouldBe(SetState.Fail);
        Table.EvaluateIdeas(new IdeaCounts(10, 9, 10, 0.849, 0.7, 2, 2)).State.ShouldBe(SetState.Fail);
        Table.EvaluateIdeas(new IdeaCounts(10, 9, 10, 0.95, 0.59, 2, 2)).State.ShouldBe(SetState.Fail);
        Table.EvaluateIdeas(new IdeaCounts(10, 9, 10, 0.95, 0.7, 1, 2)).State.ShouldBe(SetState.Fail);
    }

    [Fact]
    public void The_verdict_is_pass_only_when_data_plans_and_ideas_pass_and_events_do_not_block()
    {
        SetOutcome Ok(string set) => SetOutcome.Of(set, SetState.Pass);
        var events = SetOutcome.Of("events", SetState.NotEvaluable, "no rows");

        EvalVerdict.Of([Ok("data"), Ok("plans"), Ok("ideas"), events]).ShouldBe(EvalVerdict.Pass);
        EvalVerdict.Of([Ok("data"), Ok("plans"), events]).ShouldBe(EvalVerdict.Incomplete);
        EvalVerdict.Of([Ok("data"), SetOutcome.Of("plans", SetState.NotRun), Ok("ideas")]).ShouldBe(EvalVerdict.Incomplete);
        EvalVerdict.Of([Ok("data"), Ok("plans"), SetOutcome.Of("ideas", SetState.Fail, "x")]).ShouldBe(EvalVerdict.Fail);
        EvalVerdict.Of([Ok("data"), Ok("plans"), Ok("ideas"), SetOutcome.Of("events", SetState.Fail, "x")]).ShouldBe(EvalVerdict.Fail);
        EvalVerdict.Of([]).ShouldBe(EvalVerdict.Incomplete);
    }

    private static List<AnnouncementRow> Rows(int truePositive, int falsePositive, int falseNegative, int trueNegative)
    {
        var rows = new List<AnnouncementRow>();
        rows.AddRange(Enumerable.Range(0, truePositive).Select(i => new AnnouncementRow($"tp{i}", true, true)));
        rows.AddRange(Enumerable.Range(0, falsePositive).Select(i => new AnnouncementRow($"fp{i}", false, true)));
        rows.AddRange(Enumerable.Range(0, falseNegative).Select(i => new AnnouncementRow($"fn{i}", true, false)));
        rows.AddRange(Enumerable.Range(0, trueNegative).Select(i => new AnnouncementRow($"tn{i}", false, false)));
        return rows;
    }

    [Fact]
    public void Events_precision_of_exactly_point_eight_passes_and_point_seven_nine_five_fails()
    {
        var pass = AnnouncementGrader.Grade(Rows(160, 40, 20, 20), Table);
        var fail = AnnouncementGrader.Grade(Rows(159, 41, 20, 20), Table);

        pass.State.ShouldBe(SetState.Pass);
        pass.Precision!.Value.ShouldBe(0.80, 1e-12);
        pass.PrecisionInterval!.Value.Low.ShouldBe(0.739, 0.001);
        pass.Recall!.Value.ShouldBe(160.0 / 180, 1e-12);
        fail.State.ShouldBe(SetState.Fail);
        fail.Precision!.Value.ShouldBe(0.795, 1e-12);
    }

    [Fact]
    public void Events_with_too_few_rows_are_not_evaluable_and_say_when_they_arrive()
    {
        var result = AnnouncementGrader.Grade(Rows(10, 0, 0, 189), Table);

        result.State.ShouldBe(SetState.NotEvaluable);
        result.Note.ShouldBe("not_evaluable: 200 labelled announcements arrive with P15");
        result.Precision.ShouldBeNull();
        AnnouncementGrader.Grade([], Table).State.ShouldBe(SetState.NotEvaluable);
    }

    [Fact]
    public void Events_where_the_model_calls_nothing_material_fail_with_no_precision() =>
        AnnouncementGrader.Grade(Rows(0, 0, 50, 150), Table).ShouldSatisfyAllConditions(
            r => r.State.ShouldBe(SetState.Fail),
            r => r.Precision.ShouldBeNull(),
            r => r.Recall!.Value.ShouldBe(0.0));
}
