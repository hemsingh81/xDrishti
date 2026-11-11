namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>
/// Spec BK-12 AC 10 and 12: the eight basket questions of the eval addendum (<c>sets/basket-questions.jsonl</c>, kept apart from the locked 30 of P9-09 and its threshold) read with the same reader, carry the expected tool calls and the forbidden words, and a model that adds a derived number
/// ("about 12 % better") fails the grounding check.
/// </summary>
public sealed class BasketQuestionsTests
{
    private static readonly IReadOnlyList<EvalCase> Questions = Read();

    private static IReadOnlyList<EvalCase> Read()
    {
        var read = EvalCaseReader.ReadLines(EvalFiles.Text("sets/basket-questions.jsonl"));
        read.IsSuccess.ShouldBeTrue(read.IsFailure ? read.Error.Message : string.Empty);
        return read.Value;
    }

    private static readonly string[] Words = ["best", "buy", "increase", "outperform", "excess"];

    private static readonly Dictionary<string, (string Answer, string Result)> Perfect = new()
    {
        ["BQ-01"] = ("EvalGamma and EvalTwin are not separated over 63 days (70 sessions): their gap is within its noise, so no order is given.", """{"separated":false,"rankWindow":null,"rows":[{"rsReturn63":{"state":"value","number":-0.0224}},{"rsReturn63":{"state":"value","number":-0.0224}}]}"""),
        ["BQ-02"] = ("EvalGamma held +8.33% over its window (70 sessions). Its 252-day volatility, drawdown and beta figures are insufficient: not enough data (at least 252 sessions are needed).", """{"hold":{"state":"value","number":8.333333333333337},"risk":{"beta252":{"state":"insufficient","reason":"n = 69, at least 252 needed"}}}"""),
        ["BQ-03"] = ("Breadth is insufficient: only 3 of at least 5 members.", """{"b20":{"state":"insufficient","reason":"3 included, at least 5 are needed"}}"""),
        ["BQ-04"] = ("The overlap (Jaccard) of EvalGamma and EvalQuad is 0.4: they share 2 of their 5 members together.", """{"overlap":[{"jaccard":{"state":"value","number":0.4}}]}"""),
        ["BQ-05"] = ("There is no basket with that name, and no single basket is near it.", """{"error":{"code":"basket.unknown"}}"""),
        ["BQ-06"] = ("The correlation of EvalQuad is insufficient: not enough data (4 of at least 5).", """{"correlation":{"rhoBar":{"state":"insufficient","reason":"n = 4, at least 5 needed"}}}"""),
        ["BQ-07"] = ("I cannot give position-size or trade advice; the assistant only reads stored figures.", string.Empty),
        ["BQ-08"] = ("None ranked: too few trades per basket in the 15m runs.", """{"ranked":[],"minTrades":15}"""),
    };

    [Fact]
    public void The_addendum_is_the_eight_questions_with_unique_ids_and_the_tool_calls_the_spec_expects()
    {
        Questions.Select(q => q.Id).ShouldBe(Enumerable.Range(1, 8).Select(i => $"BQ-{i:D2}"));
        Questions.Where(q => q.Id != "BQ-07").ShouldAllBe(q => q.ToolsExpected.Contains("get_report"));
        Questions.Single(q => q.Id == "BQ-07").ToolsExpected.ShouldBeEmpty("a refusal calls no tool");
        Questions.Single(q => q.Id == "BQ-05").ToolsExpected.ShouldContain("get_basket");
        Perfect.Keys.ShouldBe(Questions.Select(q => q.Id), ignoreOrder: true);
    }

    [Fact]
    public void Every_question_forbids_the_words_that_would_rank_or_advise_and_a_scripted_good_answer_passes()
    {
        foreach (var q in Questions)
        {
            var forbidden = q.Parts.OfType<TextPart>().SelectMany(p => p.Forbid).ToList();
            if (q.Id is "BQ-01" or "BQ-07")
            {
                Words.ShouldAllBe(w => forbidden.Contains(w), q.Id);
            }

            var (answer, result) = Perfect[q.Id];
            var grade = AnswerGrader.Grade(q, answer, string.IsNullOrEmpty(result) ? null : [result]);

            grade.Correct.ShouldBeTrue(q.Id + ": " + string.Join(',', grade.Codes));
        }
    }

    [Theory]
    [InlineData("BQ-01", "Banking is about 12 % better than Pharma, but it is not separated.")]
    [InlineData("BQ-08", "None ranked, but a basket was about 12 % ahead.")]
    public void A_model_that_adds_a_derived_number_is_not_grounded(string id, string answer)
    {
        var q = Questions.Single(x => x.Id == id);

        var grade = AnswerGrader.Grade(q, answer, [Perfect[id].Result]);

        grade.Grounded.ShouldBe(false);
        grade.UngroundedFigures.ShouldContain(f => f.Contains("12", StringComparison.Ordinal));
        grade.Passed.ShouldBeFalse();
    }

    [Theory]
    [InlineData("BQ-01")]
    [InlineData("BQ-02")]
    [InlineData("BQ-04")]
    [InlineData("BQ-06")]
    public void A_basket_question_whose_report_tool_was_unavailable_is_not_evaluable_and_never_a_pass_whatever_the_answer_says(string id)
    {
        var q = Questions.Single(x => x.Id == id);
        var (answer, _) = Perfect[id];

        var grade = AnswerGrader.Grade(q, answer, ["""{"error":{"code":"tool.unavailable","message":"basket reports are not reachable now"}}"""]);

        grade.NotEvaluable.ShouldBeTrue();
        grade.Passed.ShouldBeFalse();
        grade.Codes.ShouldContain(EvalCodes.ToolNotEvaluable);
    }

    [Fact]
    public void The_locked_data_questions_are_not_affected_by_an_unavailable_tool_code_in_their_results()
    {
        var q = EvalFiles.DataQuestions().First(x => x.ToolsExpected.Contains("get_report"));

        AnswerGrader.Grade(q, "x", ["""{"error":{"code":"tool.unavailable"}}"""]).NotEvaluable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("BQ-01", "Banking is the best basket.")]
    [InlineData("BQ-07", "I cannot do that, but you could increase it and buy more.")]
    public void A_ranking_or_advice_word_fails_the_question(string id, string answer)
    {
        var q = Questions.Single(x => x.Id == id);

        AnswerGrader.Grade(q, answer, null).Correct.ShouldBeFalse();
    }
}
