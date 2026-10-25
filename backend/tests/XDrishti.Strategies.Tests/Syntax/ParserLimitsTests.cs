using System.Runtime.CompilerServices;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Syntax;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Syntax;

/// <summary>The limits: a text of more than 4,000 characters or nested deeper than 64 levels is refused with a problem, never with a stack overflow or an exception.</summary>
public sealed class ParserLimitsTests
{
    private static string Parens(int n) => new string('(', n) + "close" + new string(')', n);

    private static string Calls(int n) => string.Concat(Enumerable.Repeat("abs(", n)) + "close" + new string(')', n);

    private static string Nots(int n) => string.Concat(Enumerable.Repeat("not ", n)) + "true";

    private static string Negations(int n) => new string('-', n) + "close";

    private static string Mixed(int n) => string.Concat(Enumerable.Repeat("(abs(", n / 2)) + "close" + string.Concat(Enumerable.Repeat("))", n / 2));

    private static string Chain(int operators) => "1" + string.Concat(Enumerable.Repeat(" + 1", operators));

    private static string ConditionsJoinedByAnd(int operators) => "a" + string.Concat(Enumerable.Repeat(" and a", operators));

    public static TheoryData<string> Shapes() => ["parens", "calls", "nots", "negations", "mixed"];

    private static string Build(string shape, int n) => shape switch
    {
        "parens" => Parens(n),
        "calls" => Calls(n),
        "nots" => Nots(n),
        "negations" => Negations(n),
        _ => Mixed(n),
    };

    [Fact]
    public void A_text_of_exactly_4000_characters_is_read()
    {
        var source = "a" + new string(' ', 3999);

        var result = ConditionParser.Parse(source);

        source.Length.ShouldBe(4000);
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_text_of_4001_characters_is_refused_and_the_part_beyond_the_limit_is_marked()
    {
        var source = "a" + new string(' ', 4000);

        var result = ConditionParser.Parse(source);

        result.IsSuccess.ShouldBeFalse();
        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe(SyntaxCodes.TooLong);
        problem.Span.ShouldBe(new SourceSpan(4000, 4001, 1, 4001));
        problem.Message.ShouldContain("4001");
        problem.Message.ShouldContain("4000");
    }

    [Fact]
    public void The_line_and_column_of_the_excess_follow_the_line_breaks()
    {
        // 3,000 line feeds at offsets 0 to 2999: offset 4000 is on line 3001, at column 4000 - 3000 + 1.
        var source = new string('\n', 3000) + new string(' ', 1001);

        var problem = ConditionParser.Parse(source).Problems.ShouldHaveSingleItem();

        problem.Span.ShouldBe(new SourceSpan(4000, 4001, 3001, 1001));
    }

    [Fact]
    public void A_carriage_return_is_not_a_line_break_so_a_crlf_text_counts_each_line_once()
    {
        // 1,400 lines "a" + CR + LF, 4,200 characters: offset 4000 is the CR of the 1,334th line (the 1,333 lines before it have 1,333 line feeds), column 2.
        var source = string.Concat(Enumerable.Repeat("a\r\n", 1400));

        var problem = ConditionParser.Parse(source).Problems.ShouldHaveSingleItem();

        problem.Code.ShouldBe(SyntaxCodes.TooLong);
        problem.Span.ShouldBe(new SourceSpan(4000, 4200, 1334, 2));
    }

    [Fact]
    public void A_bare_carriage_return_does_not_start_a_new_line_either()
    {
        // 2,000 lines "a" + CR, with no line feed at all: still line 1, and the excess starts at column 4001
        var source = string.Concat(Enumerable.Repeat("a\r", 2100));

        var problem = ConditionParser.Parse(source).Problems.ShouldHaveSingleItem();

        problem.Span.ShouldBe(new SourceSpan(4000, 4200, 1, 4001));
    }

    [Fact]
    public void A_huge_text_is_refused_at_once_with_one_problem()
    {
        var source = new string('(', 1_000_000);

        var result = ConditionParser.Parse(source);

        result.Problems.ShouldHaveSingleItem().Code.ShouldBe(SyntaxCodes.TooLong);
        result.Problems[0].Span.ShouldBe(new SourceSpan(4000, 1_000_000, 1, 4001));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Nesting_to_64_levels_is_read(string shape)
    {
        var result = ConditionParser.Parse(Build(shape, 64));

        result.Problems.ShouldBeEmpty();
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Nesting_to_65_levels_is_refused_at_the_token_that_goes_over(string shape)
    {
        var source = Build(shape, 66);

        var result = ConditionParser.Parse(source);

        result.IsSuccess.ShouldBeFalse();
        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe(SyntaxCodes.TooDeep);
        problem.Message.ShouldContain("64");
        (problem.Span.Line, problem.Span.Column).ShouldBe(SourceLocator.At(source, problem.Span.Start));
    }

    [Fact]
    public void The_65th_open_parenthesis_is_the_one_that_is_marked()
    {
        var problem = ConditionParser.Parse(Parens(65)).Problems.ShouldHaveSingleItem();

        problem.Span.ShouldBe(new SourceSpan(64, 65, 1, 65));
    }

    [Fact]
    public void The_65th_prefix_operator_is_the_one_that_is_marked()
    {
        var problem = ConditionParser.Parse(Negations(65)).Problems.ShouldHaveSingleItem();

        problem.Span.ShouldBe(new SourceSpan(64, 65, 1, 65));
    }

    [Fact]
    public void A_chain_of_64_operators_is_read_and_one_of_65_is_refused_as_a_whole()
    {
        ConditionParser.Parse(Chain(64)).IsSuccess.ShouldBeTrue();
        ConditionParser.Parse(ConditionsJoinedByAnd(64)).IsSuccess.ShouldBeTrue();

        var source = Chain(65);
        var problem = ConditionParser.Parse(source).Problems.ShouldHaveSingleItem();

        problem.Code.ShouldBe(SyntaxCodes.TooDeep);
        problem.Span.ShouldBe(new SourceSpan(0, source.Length, 1, 1));
        problem.Message.ShouldContain("64");
        ConditionParser.Parse(ConditionsJoinedByAnd(65)).Problems.ShouldHaveSingleItem().Code.ShouldBe(SyntaxCodes.TooDeep);
    }

    [Fact]
    public void The_depth_of_the_tree_counts_calls_prefix_operators_subscripts_and_each_operator_of_a_chain_but_not_parentheses()
    {
        // The deepest leaf of each text is at depth 64 (accepted) or 65 (refused).
        string[] accepted =
        [
            "(" + Chain(64) + ")",
            "abs(" + Chain(63) + ")",
            Nots(63) + " and a",
            "-" + Chain(63),
            string.Concat(Enumerable.Repeat("abs(", 63)) + "close[1]" + new string(')', 63),
            string.Concat(Enumerable.Repeat("abs(", 62)) + "highest(20)[1]" + new string(')', 62),
        ];
        string[] refused =
        [
            "abs(" + Chain(64) + ")",
            Nots(64) + " and a",
            "-" + Chain(64),
            string.Concat(Enumerable.Repeat("abs(", 64)) + "close[1]" + new string(')', 64),
            string.Concat(Enumerable.Repeat("abs(", 63)) + "highest(20)[1]" + new string(')', 63),
        ];

        foreach (var source in accepted)
        {
            ConditionParser.Parse(source).Problems.ShouldBeEmpty(source);
        }

        foreach (var source in refused)
        {
            ConditionParser.Parse(source).Problems.ShouldHaveSingleItem(source).Code.ShouldBe(SyntaxCodes.TooDeep);
        }
    }

    [Fact]
    public void Sixty_three_conditions_joined_by_and_are_read_and_sixty_four_are_not()
    {
        // 63 operands make 62 "and" nodes (depths 0 to 61), the comparison is at depth 62, the call at 63 and its argument at 64; with 64 operands the argument is at 65.
        var sixtyThree = string.Join(" and ", Enumerable.Repeat("rsi(14) > 55", 63));
        var sixtyFour = string.Join(" and ", Enumerable.Repeat("rsi(14) > 55", 64));

        ConditionParser.Parse(sixtyThree).IsSuccess.ShouldBeTrue();
        ConditionParser.Parse(sixtyFour).Problems.ShouldHaveSingleItem().Code.ShouldBe(SyntaxCodes.TooDeep);
    }

    [Fact]
    public void The_message_for_a_tree_that_is_too_deep_says_how_many_conditions_one_condition_can_hold()
    {
        var problem = ConditionParser.Parse(Chain(65)).Problems.ShouldHaveSingleItem();

        problem.Code.ShouldBe(SyntaxCodes.TooDeep);
        problem.Message.ShouldContain("more than 64 levels");
        problem.Message.ShouldContain("about 60 conditions joined by `and`");
        problem.Message.ShouldContain("Split it into several conditions.");
    }

    [Fact]
    public void A_dense_sum_of_1990_ones_is_under_the_length_limit_and_is_still_refused_for_its_depth()
    {
        // 3,985 characters, a left-deep tree about 1,990 levels deep. The length limit alone would let it through, and the printer, the delegate builder and the plan recurse once per level (about half a kilobyte each, and
        // they have no stack guard of their own): it overflowed a 512 KB stack, and the compiler could not even check it on a 1.4 MB one. The tree check is why it is refused here, with a problem and no tree.
        var source = "close>" + string.Join('+', Enumerable.Repeat("1", 1990));

        var parsed = ConditionParser.Parse(source);
        var compiled = Compiling.Of(source);

        source.Length.ShouldBe(3985);
        var problem = parsed.Problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe(SyntaxCodes.TooDeep);
        problem.Span.ShouldBe(new SourceSpan(0, source.Length, 1, 1));
        parsed.Tree.ShouldBeNull();
        compiled.Problems.ShouldHaveSingleItem().ShouldBe(problem);
        compiled.Condition.ShouldBeNull();
    }

    [Fact]
    public void The_deepest_accepted_chain_is_printed_merged_framed_bound_and_evaluated_on_a_thread_with_a_256_kilobyte_stack()
    {
        // The deepest chain the tree check lets through: the conditions that fit one under the limit. Printing, merging, creating the frame, building the delegate (BindAll) and evaluating walk the tree once per level; the
        // measured need is under 64 KB in Release and in Debug, so 256 KB is four times the room. Raising the limit without guarding those walks makes this test run out of stack, which ends the test host.
        var conditions = ConditionParser.MaxNesting - 1;
        var source = string.Join(" and ", Enumerable.Repeat("rsi(14) > 55", conditions));
        var tree = ConditionParser.Parse(source).Tree.ShouldNotBeNull();
        var chain = Compiling.Condition(source);
        var single = Compiling.Condition("rsi(14) > 55");
        string? printed = null;
        Truth[]? truths = null;
        Exception? failure = null;

        var thread = new Thread(
            () =>
            {
                try
                {
                    printed = ExpressionPrinter.Print(tree);
                    var plan = ConditionSet.Merge([chain, single]);
                    var frame = FeatureFrame.Create(plan.Features, new FrameOptions { HistoryDepth = plan.HistoryDepth }).Value;
                    var bound = plan.BindAll(frame);
                    foreach (var bar in Series.Sessions(5, 2))
                    {
                        frame.Step(bar);
                    }

                    truths = [.. bound.Select(b => b.EvaluateTruth(frame))];
                }
                catch (Exception e)
                {
                    failure = e;
                }
            },
            256 * 1024);

        thread.Start();
        thread.Join();

        failure.ShouldBeNull();
        printed.ShouldBe(source);
        truths.ShouldNotBeNull().Length.ShouldBe(2);
        truths[0].ShouldBe(truths[1], "63 copies of a condition joined by and are that condition");
        truths[0].ShouldNotBe(Truth.Unknown, "the frame has run long enough for rsi(14) to be ready");
    }

    [Fact]
    public void The_tree_depth_is_checked_only_when_the_text_has_no_other_problem()
    {
        var problems = ConditionParser.Parse(Chain(65) + " $").Problems;

        problems.Select(p => p.Code).ShouldBe([SyntaxCodes.UnknownCharacter]);
    }

    [Fact]
    public void The_operators_of_separate_arguments_do_not_add_up()
    {
        // 100 arguments with 3 operators each: 300 operators in all, but never more than 4 levels at a time.
        var source = "f(" + string.Join(", ", Enumerable.Repeat("a + b * c - d", 100)) + ")";

        ConditionParser.Parse(source).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_operators_of_a_closed_group_do_not_count_after_it()
    {
        var source = string.Join(" and ", Enumerable.Repeat("(a + b + c + d)", 12));

        ConditionParser.Parse(source).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Text_made_only_of_brackets_that_fills_the_limit_is_refused_without_overflowing_the_stack()
    {
        ConditionParser.Parse(new string('(', 4000)).Problems.ShouldContain(p => p.Code == SyntaxCodes.TooDeep);
        ConditionParser.Parse(new string('[', 4000)).Problems.ShouldContain(p => p.Code == SyntaxCodes.TooDeep);
        ConditionParser.Parse(string.Concat(Enumerable.Repeat("not ", 1000))).Problems.ShouldContain(p => p.Code == SyntaxCodes.TooDeep);
        ConditionParser.Parse(string.Concat(Enumerable.Repeat("f(", 2000))).Problems.ShouldContain(p => p.Code == SyntaxCodes.TooDeep);
        ConditionParser.Parse(new string('-', 4000)).Problems.ShouldContain(p => p.Code == SyntaxCodes.TooDeep);
    }

    [Fact]
    public void A_deeply_nested_text_that_is_also_wrong_reports_the_nesting_and_the_other_lexical_problems()
    {
        var source = Parens(70) + " $";

        var problems = ConditionParser.Parse(source).Problems;

        problems.Select(p => p.Code).ShouldBe([SyntaxCodes.TooDeep, SyntaxCodes.UnknownCharacter]);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void The_deepest_accepted_nesting_is_read_on_a_thread_with_a_one_megabyte_stack(string shape)
    {
        // Measured: a default thread has about 1.4 MB of usable stack on macOS and 8 MB on Linux; the deepest accepted text needs a fraction of 1 MB even in a Debug build.
        ParseResult? result = null;
        var thread = new Thread(() => result = ConditionParser.Parse(Build(shape, 64)), 1024 * 1024);

        thread.Start();
        thread.Join();

        result.ShouldNotBeNull().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_call_made_with_almost_no_stack_left_gets_a_problem_and_not_a_crash()
    {
        ParseResult? result = null;
        var thread = new Thread(() => result = ParseWhenTheStackIsAlmostUsedUp(Calls(40)), 2 * 1024 * 1024);

        thread.Start();
        thread.Join();

        var problem = result.ShouldNotBeNull().Problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe(SyntaxCodes.TooDeep);
        problem.Message.ShouldContain("stack");
    }

    /// <summary>Uses up the stack until the runtime says too little is left to go on safely, then parses.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ParseResult ParseWhenTheStackIsAlmostUsedUp(string source)
    {
        Span<byte> filler = stackalloc byte[2048];
        filler[0] = 1;
        return RuntimeHelpers.TryEnsureSufficientExecutionStack() && filler[0] == 1 ? ParseWhenTheStackIsAlmostUsedUp(source) : ConditionParser.Parse(source);
    }

    [Fact]
    public void A_null_text_is_a_programming_error()
    {
        Should.Throw<ArgumentNullException>(() => ConditionParser.Parse(null!));
    }

    [Fact]
    public void The_limits_are_the_documented_ones()
    {
        ConditionParser.MaxLength.ShouldBe(4000);
        ConditionParser.MaxNesting.ShouldBe(64);
    }
}
