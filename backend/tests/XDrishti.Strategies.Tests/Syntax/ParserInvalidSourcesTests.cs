using XDrishti.Strategies.Syntax;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Syntax;

public sealed class ParserInvalidSourcesTests
{
    [Fact]
    public void The_table_has_more_than_sixty_cases()
    {
        InvalidSources.Cases.Count.ShouldBeGreaterThanOrEqualTo(60);
    }

    [Fact]
    public void The_table_exercises_every_syntax_code_except_the_two_limits()
    {
        var used = InvalidSources.Cases.Values.SelectMany(p => p).Select(p => p.Code).ToHashSet();

        string[] expected =
        [
            SyntaxCodes.Empty, SyntaxCodes.UnknownCharacter, SyntaxCodes.UnterminatedString, SyntaxCodes.NumberFollowedByIdentifier, SyntaxCodes.NumberOutOfRange, SyntaxCodes.UnexpectedEnd,
            SyntaxCodes.UnexpectedToken, SyntaxCodes.MissingOperand, SyntaxCodes.MissingOperator, SyntaxCodes.ChainedComparison, SyntaxCodes.UnclosedBracket, SyntaxCodes.UnmatchedClosingBracket,
            SyntaxCodes.MissingArgument, SyntaxCodes.TrailingComma, SyntaxCodes.SubscriptOnNonReference, SyntaxCodes.BadSubscript, SyntaxCodes.BadTimeframeQualifier,
        ];

        used.Intersect(expected).Count().ShouldBe(expected.Length);
    }

    [Fact]
    public void Every_syntax_code_is_in_the_table_or_is_one_of_the_three_limits_that_have_tests_of_their_own()
    {
        var all = typeof(SyntaxCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
        var used = InvalidSources.Cases.Values.SelectMany(p => p).Select(p => p.Code).ToHashSet();
        string[] limits = [SyntaxCodes.TooLong, SyntaxCodes.TooDeep, SyntaxCodes.TooManyProblems];

        all.Count.ShouldBe(20);
        all.Except(used).ShouldBe(limits, ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(InvalidSources.Sources), MemberType = typeof(InvalidSources))]
    public void An_invalid_source_gives_exactly_the_expected_problems_in_source_order(string source)
    {
        var result = ConditionParser.Parse(source);

        result.IsSuccess.ShouldBeFalse();
        result.Tree.ShouldBeNull();
        var actual = result.Problems.Select(p => new ExpectedProblem(p.Code, p.Span.Start, p.Span.End, p.Span.Line, p.Span.Column, p.Suggestion)).ToArray();
        actual.ShouldBe(InvalidSources.Cases[source]);
        result.Problems.ShouldAllBe(p => p.Severity == ProblemSeverity.Error);
    }

    [Theory]
    [MemberData(nameof(InvalidSources.Sources), MemberType = typeof(InvalidSources))]
    public void Every_problem_has_a_message_and_a_line_and_column_that_match_its_offset(string source)
    {
        var result = ConditionParser.Parse(source);

        foreach (var problem in result.Problems)
        {
            problem.Message.ShouldNotBeNullOrWhiteSpace();
            problem.Span.Line.ShouldBeGreaterThanOrEqualTo(1);
            problem.Span.Column.ShouldBeGreaterThanOrEqualTo(1);
            problem.Span.Start.ShouldBeInRange(0, source.Length);
            problem.Span.End.ShouldBeInRange(problem.Span.Start, source.Length);
            (problem.Span.Line, problem.Span.Column).ShouldBe(SourceLocator.At(source, problem.Span.Start));
        }
    }

    [Theory]
    [MemberData(nameof(InvalidSources.Sources), MemberType = typeof(InvalidSources))]
    public void The_problems_are_in_source_order(string source)
    {
        var starts = ConditionParser.Parse(source).Problems.Select(p => p.Span.Start).ToArray();

        starts.ShouldBe([.. starts.Order()]);
    }
}
