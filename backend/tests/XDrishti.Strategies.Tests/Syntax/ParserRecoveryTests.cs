using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Tests.Syntax;

/// <summary>
/// The recovery: the parser does not stop at the first problem. Lexical problems are all reported; after a structural problem the tokens are repaired (a missing value supplied, the stray tokens skipped up to the
/// next <c>and</c>, <c>or</c>, comma or closing bracket, a subscript or a broken <c>tf(…)</c> removed, open brackets closed) and the parse resumes, so independent faults each get their problem, once, in source order.
/// </summary>
public sealed class ParserRecoveryTests
{
    private static (string Code, int Start)[] CodesAndStarts(string source) => [.. ConditionParser.Parse(source).Problems.Select(p => (p.Code, p.Span.Start))];

    [Fact]
    public void Two_unknown_characters_are_two_problems()
    {
        // r0 s1 i2 (3 $4 )5 _6 >7 _8 5 9 5 10 _11 a12 n13 d14 _15 c16 l17 o18 s19 e20 _21 >22 _23 @24
        CodesAndStarts("rsi($) > 55 and close > @").ShouldBe([(SyntaxCodes.UnknownCharacter, 4), (SyntaxCodes.UnknownCharacter, 24)]);
    }

    [Fact]
    public void Every_unknown_character_of_a_long_text_is_reported()
    {
        var source = string.Join(" and ", Enumerable.Repeat("a > 1 $", 40).Select((part, i) => i % 2 == 0 ? "a > 1" : "b # 2 > 3"));

        var problems = ConditionParser.Parse(source).Problems;

        problems.Count(p => p.Code == SyntaxCodes.UnknownCharacter).ShouldBe(20);
    }

    [Fact]
    public void Independent_structural_faults_are_each_reported()
    {
        // r0 s1 i2 (3 1 4 4 5 ,6 )7 _8 >9 _10 5 11 5 12 _13 a14 n15 d16 _17 a18 _19 <20 _21 b22 _23 <24
        CodesAndStarts("rsi(14,) > 55 and a < b < c").ShouldBe([(SyntaxCodes.TrailingComma, 6), (SyntaxCodes.ChainedComparison, 24)]);
    }

    [Fact]
    public void Three_faults_of_three_kinds_come_back_in_order()
    {
        // (0 a1 _2 b3 )4 _5 a6 n7 d8 _9 r10 s11 i12 (13 1 14 4 15 ,16 )17 _18 a19 n20 d21 _22 c23 )24
        var problems = ConditionParser.Parse("(a b) and rsi(14,) and c)").Problems;

        problems.Select(p => (p.Code, p.Span.Start)).ShouldBe(
        [
            (SyntaxCodes.MissingOperator, 3),
            (SyntaxCodes.TrailingComma, 16),
            (SyntaxCodes.UnmatchedClosingBracket, 24),
        ]);
    }

    [Fact]
    public void A_lexical_problem_and_a_structural_one_are_reported_together()
    {
        CodesAndStarts("a < b < c and 5m").ShouldBe([(SyntaxCodes.ChainedComparison, 6), (SyntaxCodes.NumberFollowedByIdentifier, 14)]);
    }

    [Fact]
    public void A_problem_that_comes_earlier_in_the_text_than_the_one_found_first_is_still_listed_first()
    {
        // The missing operand after ">" is found when the text ends; the "(" that is never closed is earlier in the text.
        CodesAndStarts("rsi(14 >").ShouldBe([(SyntaxCodes.UnclosedBracket, 3), (SyntaxCodes.UnexpectedEnd, 8)]);
    }

    [Fact]
    public void Brackets_that_are_never_closed_are_each_reported()
    {
        CodesAndStarts("f(g(h(1").ShouldBe([(SyntaxCodes.UnclosedBracket, 1), (SyntaxCodes.UnclosedBracket, 3), (SyntaxCodes.UnclosedBracket, 5)]);
    }

    [Fact]
    public void A_missing_value_is_supplied_so_the_next_fault_is_found()
    {
        // "a + * b" lacks a value after "+"; "<" after "*"'s operands is then a second comparison only if the first was read: here it is a second, independent fault.
        CodesAndStarts("a + * b and c < d < e").ShouldBe([(SyntaxCodes.MissingOperand, 4), (SyntaxCodes.ChainedComparison, 18)]);
    }

    [Fact]
    public void One_typo_is_one_problem_not_a_chain()
    {
        foreach (var source in new[] { "a ＞ b", "a && b", "a || b", "'abc' == x", "x​ > 1", "rsi(14", "x[", "(a", "a >\n", "tf(15).ema(20) > 1", "a[1.5] > 1", "x[a] > 1", "f(source=1) > 1", "close[1] [2]", "a = b" })
        {
            ConditionParser.Parse(source).Problems.Count.ShouldBe(1, source);
        }
    }

    [Fact]
    public void A_broken_timeframe_qualifier_is_replaced_whole_so_what_follows_it_is_not_reported_again()
    {
        CodesAndStarts("tf(15).ema(20) > tf(\"1h\").ema(50)").ShouldBe([(SyntaxCodes.BadTimeframeQualifier, 3)]);
        CodesAndStarts("tf(15).ema(20) > tf(\"1h\")").ShouldBe([(SyntaxCodes.BadTimeframeQualifier, 3), (SyntaxCodes.BadTimeframeQualifier, 17)]);
    }

    [Fact]
    public void A_bad_subscript_is_removed_so_the_reference_before_it_is_still_read()
    {
        CodesAndStarts("close[x] > 1 and open[] < 2").ShouldBe([(SyntaxCodes.BadSubscript, 6), (SyntaxCodes.BadSubscript, 22)]);
    }

    [Fact]
    public void Stray_tokens_are_skipped_up_to_the_next_and_so_the_conditions_after_it_are_still_checked()
    {
        // "b c d" are three values side by side: one problem for "b", the rest skipped; then the "and" resumes the parse.
        CodesAndStarts("a b c d and rsi(14,)").ShouldBe([(SyntaxCodes.MissingOperator, 2), (SyntaxCodes.TrailingComma, 18)]);
    }

    [Fact]
    public void A_text_full_of_faults_ends_and_reports_them_all_once_each()
    {
        var source = string.Concat(Enumerable.Repeat("a b ", 900));

        var problems = ConditionParser.Parse(source).Problems;

        problems.Count.ShouldBe(1, "the skipped tokens up to the end of the text are one repair");
        source.Length.ShouldBe(3600);
    }

    [Fact]
    public void Many_conditions_each_with_one_fault_give_one_problem_each()
    {
        var source = string.Join(" and ", Enumerable.Repeat("rsi(14,) > 55", 100));

        var problems = ConditionParser.Parse(source).Problems;

        problems.Count.ShouldBe(100);
        problems.ShouldAllBe(p => p.Code == SyntaxCodes.TrailingComma);
    }

    [Fact]
    public void The_same_fault_in_a_valid_looking_neighbourhood_is_not_blamed_on_the_neighbours()
    {
        var problems = ConditionParser.Parse("rsi(14) > 55 and close > vwap and (a +) and ema(20) > ema(50)").Problems;

        var problem = problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe(SyntaxCodes.MissingOperand);
        problem.Span.ShouldBe(new SourceSpan(38, 39, 1, 39));
    }

    [Fact]
    public void A_valid_text_never_gets_a_problem()
    {
        foreach (var source in ValidSources.Cases.Keys)
        {
            ConditionParser.Parse(source).Problems.ShouldBeEmpty(source);
        }
    }

    [Fact]
    public void The_messages_name_the_offending_text()
    {
        ConditionParser.Parse("rsi(14) 55").Problems[0].Message.ShouldContain("`55`");
        ConditionParser.Parse("a < b < c").Problems[0].Message.ShouldContain("`<`");
        ConditionParser.Parse("a b").Problems[0].Message.ShouldContain("`b`");
        ConditionParser.Parse("55abc").Problems[0].Message.ShouldContain("55abc");
        ConditionParser.Parse("1e400").Problems[0].Message.ShouldContain("1e400");
        ConditionParser.Parse("\"abc").Problems[0].Message.ShouldContain("\"abc");
        ConditionParser.Parse("(a").Problems[0].Message.ShouldContain("`(`");
        ConditionParser.Parse("a)").Problems[0].Message.ShouldContain("`)`");
        ConditionParser.Parse("a +").Problems[0].Message.ShouldContain("`+`");
        ConditionParser.Parse("x[a]").Problems[0].Message.ShouldContain("`a`");
        ConditionParser.Parse("f(source=1)").Problems[0].Message.ShouldContain("`1`");
        ConditionParser.Parse("a = b").Problems[0].Message.ShouldContain("==");
    }
}
