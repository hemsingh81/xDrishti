namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>Spec P9-09 AC 2 to 4: the 30 questions, exact number grading, conflicts, honest states and grounding.</summary>
public sealed class AnswerGraderTests
{
    private static readonly IReadOnlyList<EvalCase> Questions = EvalFiles.DataQuestions();

    private static AnswerGrade Grade(string id, string answer, params string[] results) =>
        AnswerGrader.Grade(Questions.Single(q => q.Id == id), answer, results);

    private static EvalCase Single(ExpectedPart part, string question = "What is it?", string[]? anchors = null, EvalFraction[]? allowed = null) =>
        new("T-1", "kpi", question, [part], anchors ?? [], allowed ?? [], []);

    [Fact]
    public void The_set_is_exactly_the_thirty_questions_of_the_spec()
    {
        Questions.Select(q => q.Id).ShouldBe(Enumerable.Range(1, 30).Select(i => $"DQ-{i:D2}"));
        Questions.GroupBy(q => q.Category).ToDictionary(g => g.Key, g => g.Count()).ShouldBe(new Dictionary<string, int> { ["kpi"] = 8, ["groups"] = 6, ["portfolio"] = 5, ["plan"] = 3, ["learning"] = 4, ["honest"] = 4 });
        Questions.Count(q => q.IsHonest).ShouldBe(4);
        PerfectAnswers.ById.Keys.ShouldBe(Questions.Select(q => q.Id), ignoreOrder: true);
    }

    [Fact]
    public void A_scripted_perfect_answer_set_grades_thirty_of_thirty()
    {
        foreach (var q in Questions)
        {
            var (answer, result) = PerfectAnswers.ById[q.Id];

            var grade = AnswerGrader.Grade(q, answer, [result]);

            grade.Codes.ShouldBeEmpty(q.Id + ": " + answer);
            grade.Passed.ShouldBeTrue(q.Id);
        }
    }

    [Fact]
    public void Changing_the_last_digit_of_the_number_fails_every_numeric_question()
    {
        var numeric = Questions.Where(q => q.Parts.Any(p => p is NumberPart or IntegerPart)).ToList();
        numeric.Count.ShouldBe(22);
        var passes = 0;
        foreach (var q in numeric)
        {
            var (answer, result) = PerfectAnswers.ById[q.Id];
            var figure = AnswerNumbers.Find(answer).First(f => q.Parts.Any(p => p is NumberPart n && AnswerGrader.IsAccepted(n, f) || p is IntegerPart i && f.Value == i.Value));
            var digit = figure.End - 1;
            while (!char.IsAsciiDigit(answer[digit]))
            {
                digit--;
            }

            var changed = answer[..digit] + (char)('0' + (((answer[digit] - '0') + 1) % 10)) + answer[(digit + 1)..];

            var grade = AnswerGrader.Grade(q, changed, [result]);

            passes += grade.Correct ? 1 : 0;
        }

        passes.ShouldBe(0);
    }

    [Fact]
    public void A_perfect_answer_plus_one_unrelated_number_in_an_anchored_sentence_conflicts()
    {
        var grade = Grade("DQ-02", "The win rate of ORB 15m is 45.83% and 17 runs agree.", PerfectAnswers.ById["DQ-02"].ToolResult);

        grade.Correct.ShouldBeFalse();
        grade.Codes.ShouldContain(EvalCodes.ConflictingNumber);
    }

    [Fact]
    public void An_extra_number_in_a_sentence_that_does_not_answer_is_judged_by_grounding_not_conflict()
    {
        var withSource = Grade("DQ-02", "The win rate of ORB 15m is 45.83%. The run holds 24 trades.", """{"winRate":0.4583333,"n":24}""");
        var withoutSource = Grade("DQ-02", "The win rate of ORB 15m is 45.83%. The run holds 99 trades.", """{"winRate":0.4583333,"n":24}""");

        withSource.Passed.ShouldBeTrue();
        withoutSource.Correct.ShouldBeTrue();
        withoutSource.Grounded.ShouldBe(false);
        withoutSource.Codes.ShouldBe([EvalCodes.UngroundedNumber]);
        withoutSource.UngroundedFigures.ShouldBe(["99"]);
        withoutSource.Passed.ShouldBeFalse();
    }

    [Theory]
    [InlineData("45.83%", true)]
    [InlineData("45.8%", true)]
    [InlineData("45.833%", true)]
    [InlineData("46%", false)]
    [InlineData("45.9%", false)]
    [InlineData("45.82%", false)]
    [InlineData("0.4583", false)]
    public void Number_expectations_are_exact_after_rounding_to_the_printed_decimals(string printed, bool accepted)
    {
        var part = new NumberPart(EvalFraction.Parse("275/6")!.Value, 1, "%");

        var grade = AnswerGrader.Grade(Single(part), "The rate is " + printed, ["45.8333 0.4583333"]);

        grade.Correct.ShouldBe(accepted);
    }

    [Theory]
    [InlineData("0.1667", true)]
    [InlineData("0.17", true)]
    [InlineData("0.167", true)]
    [InlineData("0.16", false)]
    [InlineData("0.2", false)]
    public void An_expectancy_of_a_sixth_accepts_its_roundings_to_two_or_more_decimals(string printed, bool accepted)
    {
        var part = new NumberPart(EvalFraction.Parse("1/6")!.Value, 2, null);

        AnswerGrader.Grade(Single(part), "It is " + printed + " R", ["0.1666666"]).Correct.ShouldBe(accepted);
    }

    [Fact]
    public void Too_few_decimals_is_named()
    {
        var part = new NumberPart(EvalFraction.Parse("275/6")!.Value, 1, "%");

        AnswerGrader.Grade(Single(part), "About 46%", ["45.83"]).Codes.ShouldContain(EvalCodes.TooFewDecimals);
        AnswerGrader.Grade(Single(part), "No figure at all", ["45.83"]).Codes.ShouldContain(EvalCodes.MissingNumber);
        AnswerGrader.Grade(Single(part), "It is 12.5%", ["12.5"]).Codes.ShouldContain(EvalCodes.WrongNumber);
    }

    [Theory]
    [InlineData("It is -0.5", true)]
    [InlineData("It is −0.5", true)]
    [InlineData("It is 0.5", false)]
    [InlineData("It is -0.50", true)]
    [InlineData("It is -0.4", false)]
    public void The_sign_counts_and_a_unicode_minus_is_a_minus(string answer, bool accepted) =>
        AnswerGrader.Grade(Single(new NumberPart(EvalFraction.Of(-0.5m), 1, "R")), answer, ["-0.5 0.5 0.50"]).Correct.ShouldBe(accepted);

    [Theory]
    [InlineData("There are 24 trades", true)]
    [InlineData("There are 24.0 trades", false)]
    [InlineData("There are 24% trades", false)]
    [InlineData("There are 2 trades", false)]
    [InlineData("There are 240 trades", false)]
    public void Integer_expectations_need_the_exact_integer_token(string answer, bool accepted) =>
        AnswerGrader.Grade(Single(new IntegerPart(24)), answer, ["24 2 240 24.0"]).Correct.ShouldBe(accepted);

    [Theory]
    [InlineData("a cat", false)]
    [InlineData("zero", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("the plan for 2026-03-03 at 15m on EVH1", false)]
    public void An_answer_with_no_digit_never_passes_a_number_expectation(string answer, bool accepted)
    {
        foreach (var part in new ExpectedPart[] { new IntegerPart(0), new NumberPart(EvalFraction.Of(0), 0, null), new NumberPart(EvalFraction.Parse("1/3")!.Value, 2, null) })
        {
            AnswerGrader.Grade(Single(part), answer, []).Passed.ShouldBe(accepted);
        }
    }

    [Fact]
    public void Text_expectations_match_whole_words_without_case_and_forbidden_words_fail()
    {
        var part = new TextPart(["EVC"], ["EVD"]);
        var evalCase = Single(part);

        AnswerGrader.Grade(evalCase, "made by evc", []).Correct.ShouldBeTrue();
        AnswerGrader.Grade(evalCase, "made by EVC1", []).Codes.ShouldBe([EvalCodes.MissingText]);
    }

    [Fact]
    public void Missing_and_forbidden_text_have_their_codes()
    {
        var evalCase = Single(new TextPart(["EVC"], ["EVD"]));

        AnswerGrader.Grade(evalCase, "made by EVA", []).Codes.ShouldBe([EvalCodes.MissingText]);
        AnswerGrader.Grade(evalCase, "EVC, not EVD", []).Codes.ShouldBe([EvalCodes.ForbiddenText]);
    }

    [Fact]
    public void An_honest_answer_needs_a_state_phrase_and_no_number_for_the_withheld_metric()
    {
        var evalCase = Questions.Single(q => q.Id == "DQ-29");

        AnswerGrader.Grade(evalCase, "The win rate is 0% (0 of 2).", ["""{"n":2,"wins":0}"""]).Codes.ShouldContain(EvalCodes.WithheldNumberGiven);
        AnswerGrader.Grade(evalCase, "The win rate of EVA SELL is 0.5.", ["0.5 2"]).Codes.ShouldContain(EvalCodes.HonestStateMissing);
        AnswerGrader.Grade(evalCase, "Too few trades to say.", ["2"]).Passed.ShouldBeTrue();
        AnswerGrader.Grade(evalCase, "Insufficient data: the win rate would be 50.0%.", ["2"]).Codes.ShouldContain(EvalCodes.WithheldNumberGiven);
    }

    [Fact]
    public void An_ungrounded_figure_in_an_honest_case_is_a_hard_fail_and_in_a_normal_case_it_is_not()
    {
        var honest = Grade("DQ-28", "The net expectancy is not available. Gross expectancy is 0.31 R.", """{"state":"unavailable"}""");
        var normal = Grade("DQ-01", "ORB 15m has 24 trades and 5 symbols.", """{"n":24}""");

        honest.HardFail.ShouldBeTrue();
        normal.HardFail.ShouldBeFalse();
        normal.Passed.ShouldBeFalse();
    }

    [Fact]
    public void Missing_tool_results_make_grounding_not_evaluable_and_the_case_cannot_pass()
    {
        var grade = AnswerGrader.Grade(Questions.Single(q => q.Id == "DQ-01"), "ORB 15m has 24 trades.", null);

        grade.Correct.ShouldBeTrue();
        grade.Grounded.ShouldBeNull();
        grade.Passed.ShouldBeFalse();
        grade.Codes.ShouldBe([EvalCodes.GroundingNotEvaluable]);
    }

    [Theory]
    [InlineData("The rate is 45.83%.", """{"winRate":0.4583333333}""", true)]
    [InlineData("The rate is 45.83%.", """{"winRate":45.8333333}""", true)]
    [InlineData("The rate is 45.83%.", """{"winRate":45.84}""", false)]
    [InlineData("The rate is 45.83%.", """{"winRate":0.46}""", false)]
    public void A_figure_is_grounded_when_a_tool_result_holds_it_or_its_percent_form_at_the_printed_decimals(string answer, string result, bool grounded) =>
        AnswerGrader.Grade(Single(new TextPart(["rate"], [])), answer, [result]).Grounded.ShouldBe(grounded);

    [Fact]
    public void Numbers_of_the_question_and_the_allowed_numbers_are_grounded_without_a_tool_result()
    {
        var evalCase = Single(new TextPart(["EVD"], []), "Which one is rank 2?", null, [EvalFraction.Parse("0.275")!.Value]);

        AnswerGrader.Grade(evalCase, "EVD is rank 2 with 0.275 R and 0.28 R.", []).Grounded.ShouldBe(true);
        AnswerGrader.Grade(evalCase, "EVD is rank 3.", []).Grounded.ShouldBe(false);
        AnswerGrader.Grade(evalCase, "EVD, 0.3 R.", []).Grounded.ShouldBe(false);
    }
}
