using XDrishti.Strategies.Syntax;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Syntax;

/// <summary>
/// The recovery parses the whole text again after every repair, so a text of 4,000 characters made of nothing but faults used to cost thousands of parses (0.7 s and 1.5 GB for 3,990 commas after <c>f(</c>). The work is
/// now bounded by counting, not by a clock: the recovery reports at most <see cref="Recovery.MaxProblems"/> problems, parses at most <see cref="Recovery.MaxAttempts"/> times, and says that there are more. These tests
/// feed it the worst shapes that fit in the length limit and count the parses.
/// </summary>
public sealed class ParserWorkBoundTests
{
    /// <summary>Texts of at most 4,000 characters whose faults are many (each repair is one more parse of the whole text), by shape.</summary>
    private static readonly Dictionary<string, string> Worst = new()
    {
        ["commas after an open call"] = "f(" + new string(',', 3990),
        ["commas after a longer call"] = "rsi(" + new string(',', 3900),
        ["single ampersands"] = new string('&', 3999),
        ["double ampersands"] = string.Concat(Enumerable.Repeat("&&", 2000)),
        ["closing brackets"] = new string(')', 4000),
        ["a long sum and then closing brackets"] = string.Concat(Enumerable.Repeat("1+", 1000)) + new string(')', 2000),
        ["opening brackets and commas"] = new string('(', 60) + string.Concat(Enumerable.Repeat(", ", 1900)),
        ["a trailing comma in every condition"] = string.Join(" and ", Enumerable.Repeat("rsi(14,) > 55", 300))[..4000],
        ["lone ands"] = string.Concat(Enumerable.Repeat("and ", 1000)),
        ["lone commas"] = string.Concat(Enumerable.Repeat(", ", 2000)),
        ["unknown characters"] = string.Concat(Enumerable.Repeat("$ ", 2000)),
        ["a bang after every value"] = string.Concat(Enumerable.Repeat("close ! ", 500)),
        ["stray equals signs"] = string.Concat(Enumerable.Repeat("= ", 2000)),
        ["broken subscripts"] = string.Concat(Enumerable.Repeat("close[a] ", 400)),
        ["broken qualifiers"] = string.Concat(Enumerable.Repeat("tf( ", 900)),
        ["chained comparisons"] = string.Concat(Enumerable.Repeat("a < b < ", 500)),
        ["values side by side"] = string.Concat(Enumerable.Repeat("a ", 2000)),
        ["dots"] = string.Concat(Enumerable.Repeat("a. ", 1300)),

        // repairs that report nothing (they open or close a bracket the text did not): only the attempt bound stops them, one parse at a time, with a problem or two listed
        ["a subscript bracket opened and a parenthesis closed"] = BracketShape("[ ) "),
        ["a subscript bracket opened and closed with nothing in it"] = BracketShape("[ ] "),
        ["a parenthesis closed and a subscript bracket opened"] = BracketShape(") [ "),
        ["a subscript bracket closed and another opened"] = BracketShape("] [ "),
    };

    /// <summary>The four shapes of brackets that mend silently: 1,000 repeats of a unit of four characters, 4,000 in all.</summary>
    private static readonly string[] SilentShapes =
    [
        "a subscript bracket opened and a parenthesis closed", "a subscript bracket opened and closed with nothing in it", "a parenthesis closed and a subscript bracket opened", "a subscript bracket closed and another opened",
    ];

    private static string BracketShape(string unit) => string.Concat(Enumerable.Repeat(unit, 1000));

    public static TheoryData<string> SilentShapeNames { get; } = [.. SilentShapes];

    public static TheoryData<string> Shapes { get; } = [.. Worst.Keys];

    [Fact]
    public void The_limits_are_the_ones_the_documents_state()
    {
        Recovery.MaxProblems.ShouldBe(100);
        Recovery.MaxAttempts.ShouldBe(232);
        ConditionParser.MaxProblems.ShouldBe(Recovery.MaxProblems);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void The_tokens_are_parsed_a_bounded_number_of_times_however_many_faults_the_text_has(string shape)
    {
        var source = Worst[shape];
        source.Length.ShouldBeLessThanOrEqualTo(ConditionParser.MaxLength);
        var (tokens, problems) = LexicalPass.Run(source);
        var lexical = problems.Count;

        Recovery.Run(source, tokens, problems, out var outcome);

        outcome.Attempts.ShouldBeGreaterThan(0);
        outcome.Attempts.ShouldBeLessThanOrEqualTo(Recovery.MaxAttempts, "this count is what bounds the time; a text of 4,000 characters used to need up to 4,000");
        problems.Count.ShouldBeLessThanOrEqualTo(Math.Max(lexical, Recovery.MaxProblems), "the recovery adds nothing beyond the limit (the lexical problems, found before it, are cut by the parser)");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void However_many_faults_there_are_at_most_the_limit_is_listed_and_the_last_problem_says_that_there_are_more(string shape)
    {
        var source = Worst[shape];

        var result = ConditionParser.Parse(source);

        result.IsSuccess.ShouldBeFalse();
        result.Problems.Count.ShouldBeLessThanOrEqualTo(ConditionParser.MaxProblems + 1);
        var listed = result.Problems.Where(p => p.Code != SyntaxCodes.TooManyProblems).ToList();
        listed.Count.ShouldBeLessThanOrEqualTo(ConditionParser.MaxProblems);
        result.Problems.Count(p => p.Code == SyntaxCodes.TooManyProblems).ShouldBeLessThanOrEqualTo(1);
        if (result.Problems.Count > listed.Count)
        {
            result.Problems[^1].Code.ShouldBe(SyntaxCodes.TooManyProblems, "the note comes after the problems it stands in for");
            listed.Count.ShouldBeGreaterThan(0);
        }

        result.Problems.Select(p => p.Span.Start).ShouldBeInOrder();
    }

    [Theory]
    [MemberData(nameof(SilentShapeNames))]
    public void A_text_of_repairs_that_report_nothing_stops_at_exactly_the_attempt_bound_and_says_that_there_is_more(string shape)
    {
        // The problem cap cannot stop these (they list one problem or two); only the bound on the parses does. Without it each of them is parsed 1,001 times.
        var source = Worst[shape];
        var (tokens, problems) = LexicalPass.Run(source);

        Recovery.Run(source, tokens, problems, out var outcome);
        var result = ConditionParser.Parse(source);

        outcome.Attempts.ShouldBe(Recovery.MaxAttempts);
        outcome.Truncated.ShouldBeTrue();
        result.Problems.Count.ShouldBeLessThan(ConditionParser.MaxProblems, "it is the attempts that ran out, not the problems");
        result.Problems[^1].Code.ShouldBe(SyntaxCodes.TooManyProblems);
    }

    [Theory]
    [MemberData(nameof(SilentShapeNames))]
    public void The_note_of_a_text_that_stopped_before_the_limit_does_not_claim_a_limit(string shape)
    {
        var note = ConditionParser.Parse(Worst[shape]).Problems[^1];

        note.Code.ShouldBe(SyntaxCodes.TooManyProblems);
        note.Message.ShouldNotContain("limit", Case.Insensitive);
        note.Message.ShouldContain("Fix them and read the condition again to see the rest.");
    }

    [Fact]
    public void The_note_of_a_text_that_reached_the_limit_says_how_many_are_shown_and_not_that_the_limit_is_100()
    {
        var source = string.Join(" and ", Enumerable.Repeat("rsi(14,) > 55", ConditionParser.MaxProblems + 1));

        var note = ConditionParser.Parse(source).Problems[^1];

        note.Message.ShouldBe("There are more problems than are listed: the first 100 are shown. Fix them and read the condition again to see the rest.");
    }

    [Fact]
    public void The_problems_listed_are_the_first_100_found_and_an_unclosed_bracket_is_found_after_the_commas_inside_it()
    {
        // The recovery reports a missing bracket when it reaches the end of the text, after every comma inside it, so with enough faults before it the bracket is cut off although it stands first in the text.
        string Open(int conditions) => "(" + string.Join(" and ", Enumerable.Repeat("rsi(14,) > 55", conditions));

        var within = ConditionParser.Parse(Open(ConditionParser.MaxProblems - 1)).Problems;
        var over = ConditionParser.Parse(Open(150)).Problems;

        within.Count.ShouldBe(ConditionParser.MaxProblems, "99 commas and the bracket are 100 problems, all listed");
        within[0].Code.ShouldBe(SyntaxCodes.UnclosedBracket, "in the order of the text the bracket comes first");
        within.Skip(1).ShouldAllBe(p => p.Code == SyntaxCodes.TrailingComma);
        over.Count.ShouldBe(ConditionParser.MaxProblems + 1);
        over.Take(ConditionParser.MaxProblems).ShouldAllBe(p => p.Code == SyntaxCodes.TrailingComma);
        over.ShouldNotContain(p => p.Code == SyntaxCodes.UnclosedBracket, "found after the 100th problem, so not listed");
        over[^1].Code.ShouldBe(SyntaxCodes.TooManyProblems);
    }

    [Fact]
    public void The_unknown_characters_are_found_before_the_syntax_problems_so_100_of_them_cut_off_an_earlier_syntax_problem()
    {
        // The lexical pass runs over the whole text before the grammar: a trailing comma at offset 6 is the first problem in the text, and it is not listed when 100 unknown characters come after it.
        const string Start = "rsi(14,) > 55 and close > vwap ";

        var within = ConditionParser.Parse(Start + string.Concat(Enumerable.Repeat("$ ", ConditionParser.MaxProblems - 1))).Problems;
        var over = ConditionParser.Parse(Start + string.Concat(Enumerable.Repeat("$ ", 150))).Problems;

        within.Count.ShouldBe(ConditionParser.MaxProblems, "the comma and 99 unknown characters are 100 problems, all listed");
        within[0].Code.ShouldBe(SyntaxCodes.TrailingComma);
        within[0].Span.Start.ShouldBe(6);
        over.Count.ShouldBe(ConditionParser.MaxProblems + 1);
        over.Take(ConditionParser.MaxProblems).ShouldAllBe(p => p.Code == SyntaxCodes.UnknownCharacter);
        over.ShouldNotContain(p => p.Code == SyntaxCodes.TrailingComma, "found after the lexical problems, so cut off although it comes first in the text");
        over[^1].Code.ShouldBe(SyntaxCodes.TooManyProblems);
    }

    [Fact]
    public void Several_shapes_are_cut_off_and_say_so_so_the_test_above_does_not_pass_by_having_nothing_to_cap()
    {
        string[] mustBeCut = ["commas after an open call", "single ampersands", "closing brackets", "a long sum and then closing brackets", "lone ands", "lone commas", "unknown characters", "a trailing comma in every condition"];

        foreach (var shape in mustBeCut)
        {
            var result = ConditionParser.Parse(Worst[shape]);

            result.Problems.Count.ShouldBe(ConditionParser.MaxProblems + 1, shape);
            result.Problems[^1].Code.ShouldBe(SyntaxCodes.TooManyProblems, shape);
        }
    }

    [Fact]
    public void A_text_with_exactly_the_limit_of_problems_lists_them_all_without_a_note()
    {
        var source = string.Join(" and ", Enumerable.Repeat("rsi(14,) > 55", ConditionParser.MaxProblems));

        var problems = ConditionParser.Parse(source).Problems;

        problems.Count.ShouldBe(ConditionParser.MaxProblems);
        problems.ShouldAllBe(p => p.Code == SyntaxCodes.TrailingComma);
    }

    [Fact]
    public void One_problem_over_the_limit_cuts_the_list_at_the_limit_and_the_note_marks_the_rest_of_the_text()
    {
        var source = string.Join(" and ", Enumerable.Repeat("rsi(14,) > 55", ConditionParser.MaxProblems + 1));

        var problems = ConditionParser.Parse(source).Problems;

        problems.Count.ShouldBe(ConditionParser.MaxProblems + 1);
        problems.Take(ConditionParser.MaxProblems).ShouldAllBe(p => p.Code == SyntaxCodes.TrailingComma);
        var note = problems[^1];
        note.Code.ShouldBe(SyntaxCodes.TooManyProblems);
        note.Severity.ShouldBe(ProblemSeverity.Error);
        note.Message.ShouldContain("100", Case.Sensitive);
        note.Span.Start.ShouldBe(problems[^2].Span.End, "the note starts where the last listed problem ends");
        note.Span.End.ShouldBe(source.Length, "and covers the rest of the text");
        (note.Span.Line, note.Span.Column).ShouldBe(SourceLocator.At(source, note.Span.Start));
    }

    [Fact]
    public void Lexical_problems_are_cut_at_the_limit_too_and_stay_in_source_order()
    {
        var source = string.Concat(Enumerable.Repeat("close > 1 $\n", 150));

        var result = ConditionParser.Parse(source);

        result.Problems.Count.ShouldBe(ConditionParser.MaxProblems + 1);
        result.Problems.Take(ConditionParser.MaxProblems).ShouldAllBe(p => p.Code == SyntaxCodes.UnknownCharacter);
        result.Problems[^1].Code.ShouldBe(SyntaxCodes.TooManyProblems);
        result.Problems[^1].Span.Line.ShouldBe(100, "the 100th problem is on line 100, and the note starts right after it");
        result.Problems.Select(p => p.Span.Start).ShouldBeInOrder();
    }

    [Fact]
    public void The_list_of_problems_of_a_text_that_fits_the_limit_is_the_same_whatever_follows_in_the_other_conditions()
    {
        // the cap must not change what is reported for a text that is under it: the first conditions of a long text give the problems they give alone
        var alone = ConditionParser.Parse(string.Join(" and ", Enumerable.Repeat("rsi(14,) > 55", 10))).Problems;
        var longer = ConditionParser.Parse(string.Join(" and ", Enumerable.Repeat("rsi(14,) > 55", 60))).Problems;

        longer.Take(10).Select(p => (p.Code, p.Span.Start, p.Span.End)).ShouldBe(alone.Select(p => (p.Code, p.Span.Start, p.Span.End)));
    }

    [Fact]
    public void A_clean_text_never_gets_a_note_and_is_parsed_once()
    {
        const string Source = "rsi(14) > 55 and close > vwap";
        var (tokens, problems) = LexicalPass.Run(Source);

        var tree = Recovery.Run(Source, tokens, problems, out var outcome);

        tree.ShouldNotBeNull();
        outcome.Attempts.ShouldBe(1);
        outcome.Truncated.ShouldBeFalse();
        problems.ShouldBeEmpty();
    }
}
