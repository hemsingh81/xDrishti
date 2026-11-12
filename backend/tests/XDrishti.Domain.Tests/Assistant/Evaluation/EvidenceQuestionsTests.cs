namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>
/// AI-01 AC 4 (ADR 0280): the four evidence questions of the eval addendum (<c>sets/evidence-questions.jsonl</c>, kept apart from the locked 30 and the basket questions) read with the same reader, each forbids "ready to trade", "guaranteed" and "buy", a scripted good answer passes, an advice or
/// readiness-overstating word fails it, and a question whose <c>get_evidence</c> was unavailable is not evaluable. The tool results are the shapes of the real API over the fixture (<c>EvalEvidenceGoldenTests</c>).
/// </summary>
public sealed class EvidenceQuestionsTests
{
    private static readonly IReadOnlyList<EvalCase> Questions = Read();

    private static IReadOnlyList<EvalCase> Read()
    {
        var read = EvalCaseReader.ReadLines(EvalFiles.Text("sets/evidence-questions.jsonl"));
        read.IsSuccess.ShouldBeTrue(read.IsFailure ? read.Error.Message : string.Empty);
        return read.Value;
    }

    private static readonly string[] OnlyGetEvidence = ["get_evidence"];

    private static readonly string[] Forbidden = ["ready to trade", "guaranteed", "buy"];

    private static readonly Dictionary<string, (string Answer, string Result)> Perfect = new()
    {
        ["EQ-01"] = (
            "The last replay is research only and hindsight-labelled. It returned +1.2% before costs over 3 sessions. Its labels include filters_without_history and survivorship.",
            """{"kind":"replay_run","fence":"Replay is research only, not evidence","body":{"hindsight":[{"code":"filters_without_history"},{"code":"survivorship"}],"costsLabel":"before costs (charge sheet not filled, ISS-25)","completeness":{"sessionsDone":3},"figures":{"returnPct":1.200}}}"""),
        ["EQ-02"] = (
            "The strategy ORB is not ready: its readiness label is Not ready, with a score of 0.",
            """{"kind":"readiness_card","body":{"strategy":"ORB","version":1,"score":0,"label":"not_ready","labelText":"Not ready"}}"""),
        ["EQ-03"] = (
            "The Effect sub-score is unavailable: there is no forward mean, spread or effective n yet.",
            """{"kind":"readiness_card","body":{"subScores":[{"name":"Effect","state":"unavailable","score":null,"reading":"no forward mean, spread or effective n"}]}}"""),
        ["EQ-04"] = (
            "The shadow record has a mean R of 0.31 over 12 graded candidates, with a 90% interval up to 0.82. It is a separate count from the paper trades.",
            """{"kind":"shadow_summary","body":{"separateCount":"It is a separate count from the paper trades of armed tickets.","groups":[{"group":"all","n":12,"meanR":{"state":"value","number":0.30833333333333335},"low90":{"state":"value","number":-0.20833333333333334},"high90":{"state":"value","number":0.8166666666666668}}]}}"""),
    };

    [Fact]
    public void The_addendum_is_four_questions_that_all_call_get_evidence()
    {
        Questions.Select(q => q.Id).ShouldBe(["EQ-01", "EQ-02", "EQ-03", "EQ-04"]);
        Questions.ShouldAllBe(q => q.ToolsExpected.SequenceEqual(OnlyGetEvidence));
        Perfect.Keys.ShouldBe(Questions.Select(q => q.Id), ignoreOrder: true);
    }

    [Fact]
    public void Every_question_forbids_ready_to_trade_guaranteed_and_buy_and_a_scripted_good_answer_passes()
    {
        foreach (var q in Questions)
        {
            var forbidden = q.Parts.OfType<TextPart>().SelectMany(p => p.Forbid).ToList();
            Forbidden.ShouldAllBe(w => forbidden.Contains(w), q.Id);

            var (answer, result) = Perfect[q.Id];
            var grade = AnswerGrader.Grade(q, answer, [result]);

            grade.Correct.ShouldBeTrue(q.Id + ": " + string.Join(',', grade.Codes));
        }
    }

    [Theory]
    [InlineData("EQ-01", "The last replay returned +1.2% before costs with survivorship and filters_without_history, so buy more.")]
    [InlineData("EQ-02", "ORB is ready to trade.")]
    [InlineData("EQ-04", "The shadow mean R is 0.31 with a 90% interval up to 0.82, a separate count; it is guaranteed to continue.")]
    public void An_advice_or_overstating_word_fails_the_question(string id, string answer)
    {
        var q = Questions.Single(x => x.Id == id);

        AnswerGrader.Grade(q, answer, [Perfect[id].Result]).Correct.ShouldBeFalse();
    }

    [Fact]
    public void A_dropped_label_fails_the_replay_question()
    {
        var q = Questions.Single(x => x.Id == "EQ-01");

        var grade = AnswerGrader.Grade(q, "The last replay is research only. It returned +1.2% before costs with filters_without_history.", [Perfect["EQ-01"].Result]);

        grade.Correct.ShouldBeFalse();
    }

    [Theory]
    [InlineData("EQ-01")]
    [InlineData("EQ-02")]
    [InlineData("EQ-03")]
    [InlineData("EQ-04")]
    public void An_evidence_question_whose_tool_was_unavailable_is_not_evaluable_and_never_a_pass_whatever_the_answer_says(string id)
    {
        var q = Questions.Single(x => x.Id == id);

        var grade = AnswerGrader.Grade(q, Perfect[id].Answer, ["""{"error":{"code":"tool.unavailable","message":"stored results are not reachable now"}}"""]);

        grade.NotEvaluable.ShouldBeTrue();
        grade.Passed.ShouldBeFalse();
        grade.Codes.ShouldContain(EvalCodes.ToolNotEvaluable);
    }
}
