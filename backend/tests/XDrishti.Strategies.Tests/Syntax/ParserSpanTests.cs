using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Tests.Syntax;

/// <summary>
/// The spans of the nodes, worked out by hand (zero-based offsets, the end exclusive, line and column of the start from 1). A node covers the text it was read from; a parenthesised expression covers its
/// parentheses, so the node that contains it starts at the opening one.
/// </summary>
public sealed class ParserSpanTests
{
    private static T Parse<T>(string source)
        where T : Expression
    {
        var result = ConditionParser.Parse(source);
        result.Problems.ShouldBeEmpty();
        return result.Tree.ShouldBeOfType<T>();
    }

    private static void ShouldBeSpan(SourceSpan actual, int start, int end, int line, int column) => actual.ShouldBe(new SourceSpan(start, end, line, column));

    [Fact]
    public void A_comparison_covers_both_sides_and_each_side_covers_its_own_text()
    {
        // r0 s1 i2 (3 1 4 4 5 )6 _7 >8 _9 5 10 5 11
        var tree = Parse<Binary>("rsi(14) > 55");

        ShouldBeSpan(tree.Span, 0, 12, 1, 1);
        var call = tree.Left.ShouldBeOfType<Call>();
        ShouldBeSpan(call.Span, 0, 7, 1, 1);
        ShouldBeSpan(call.Name.Span, 0, 3, 1, 1);
        ShouldBeSpan(call.Arguments[0].Span, 4, 6, 1, 5);
        ShouldBeSpan(call.Arguments[0].ShouldBeOfType<PositionalArgument>().Value.Span, 4, 6, 1, 5);
        ShouldBeSpan(tree.Right.Span, 10, 12, 1, 11);
    }

    [Fact]
    public void A_parenthesised_expression_covers_its_parentheses()
    {
        // (0 a1 _2 +3 _4 b5 )6 _7 *8 _9 c10
        var tree = Parse<Binary>("(a + b) * c");

        ShouldBeSpan(tree.Span, 0, 11, 1, 1);
        var sum = tree.Left.ShouldBeOfType<Binary>();
        ShouldBeSpan(sum.Span, 0, 7, 1, 1);
        ShouldBeSpan(sum.Left.Span, 1, 2, 1, 2);
        ShouldBeSpan(sum.Right.Span, 5, 6, 1, 6);
        ShouldBeSpan(tree.Right.Span, 10, 11, 1, 11);
    }

    [Fact]
    public void Nested_parentheses_widen_the_node_to_the_outermost_pair_and_keep_the_name_where_it_is()
    {
        // (0 _1 (2 a3 )4 _5 )6
        var tree = Parse<Reference>("( (a) )");

        ShouldBeSpan(tree.Span, 0, 7, 1, 1);
        ShouldBeSpan(tree.Name.Span, 3, 4, 1, 4);
    }

    [Fact]
    public void A_timeframe_call_covers_tf_the_string_the_name_and_the_arguments()
    {
        // t0 f1 (2 "3 1 4 h5 "6 )7 .8 e9 m10 a11 (12 2 13 0 14 )15
        var tree = Parse<Call>("tf(\"1h\").ema(20)");

        ShouldBeSpan(tree.Span, 0, 16, 1, 1);
        ShouldBeSpan(tree.Timeframe!.Span, 3, 7, 1, 4);
        ShouldBeSpan(tree.Name.Span, 9, 12, 1, 10);
        ShouldBeSpan(tree.Arguments[0].Span, 13, 15, 1, 14);
    }

    [Fact]
    public void A_dotted_reference_covers_the_scope_the_dot_and_the_name()
    {
        var tree = Parse<Reference>("prev_day.high");

        ShouldBeSpan(tree.Span, 0, 13, 1, 1);
        ShouldBeSpan(tree.Scope!.Span, 0, 8, 1, 1);
        ShouldBeSpan(tree.Name.Span, 9, 13, 1, 10);
    }

    [Fact]
    public void A_qualified_reference_covers_the_qualifier_and_the_name()
    {
        // t0 f1 (2 "3 1 4 m5 "6 )7 .8 c9 ...
        var tree = Parse<Reference>("tf(\"1m\").close");

        ShouldBeSpan(tree.Span, 0, 14, 1, 1);
        ShouldBeSpan(tree.Timeframe!.Span, 3, 7, 1, 4);
        ShouldBeSpan(tree.Name.Span, 9, 14, 1, 10);
    }

    [Fact]
    public void A_subscript_covers_the_reference_and_the_brackets_and_knows_where_its_number_is()
    {
        // c0 l1 o2 s3 e4 [5 1 6 ]7
        var tree = Parse<Subscript>("close[1]");

        ShouldBeSpan(tree.Span, 0, 8, 1, 1);
        ShouldBeSpan(tree.Target.Span, 0, 5, 1, 1);
        ShouldBeSpan(tree.LagSpan, 6, 7, 1, 7);
    }

    [Fact]
    public void The_number_of_a_subscript_includes_its_minus_sign()
    {
        // [5 -6 1 7 2 8 ]9
        var tree = Parse<Subscript>("close[-12]");

        tree.Lag.ShouldBe(-12);
        ShouldBeSpan(tree.Span, 0, 10, 1, 1);
        ShouldBeSpan(tree.LagSpan, 6, 9, 1, 7);
    }

    [Fact]
    public void A_named_argument_covers_name_equals_and_value()
    {
        // e0 m1 a2 (3 2 4 0 5 ,6 _7 s8 o9 u10 r11 c12 e13 =14 h15 i16 g17 h18 )19
        var tree = Parse<Call>("ema(20, source=high)");

        ShouldBeSpan(tree.Span, 0, 20, 1, 1);
        var named = tree.Arguments[1].ShouldBeOfType<NamedArgument>();
        ShouldBeSpan(named.Span, 8, 19, 1, 9);
        ShouldBeSpan(named.Name.Span, 8, 14, 1, 9);
        ShouldBeSpan(named.Value.Span, 15, 19, 1, 16);
    }

    [Fact]
    public void Prefix_operators_cover_the_operator_and_the_operand()
    {
        var not = Parse<Unary>("not a");
        var negate = Parse<Unary>("-x");

        ShouldBeSpan(not.Span, 0, 5, 1, 1);
        ShouldBeSpan(not.Operand.Span, 4, 5, 1, 5);
        ShouldBeSpan(negate.Span, 0, 2, 1, 1);
        ShouldBeSpan(negate.Operand.Span, 1, 2, 1, 2);
    }

    [Fact]
    public void A_string_literal_covers_its_quotes()
    {
        var tree = Parse<StringLiteral>("\"09:45\"");

        tree.Value.ShouldBe("09:45");
        ShouldBeSpan(tree.Span, 0, 7, 1, 1);
    }

    [Fact]
    public void White_space_around_the_condition_is_not_part_of_any_span()
    {
        var tree = Parse<Reference>("  a  ");

        ShouldBeSpan(tree.Span, 2, 3, 1, 3);
    }

    [Fact]
    public void Lines_and_columns_follow_the_line_breaks()
    {
        // a0 \n1 _2 _3 a4 n5 d6 _7 b8
        var tree = Parse<Binary>("a\n  and b");

        ShouldBeSpan(tree.Span, 0, 9, 1, 1);
        ShouldBeSpan(tree.Left.Span, 0, 1, 1, 1);
        ShouldBeSpan(tree.Right.Span, 8, 9, 2, 7);
    }

    [Fact]
    public void A_carriage_return_before_the_line_feed_is_part_of_the_line_before()
    {
        // a0 \r1 \n2 b3
        var tree = Parse<Binary>("a\r\n+ b");

        ShouldBeSpan(tree.Right.Span, 5, 6, 2, 3);
    }

    [Fact]
    public void A_span_knows_its_length_and_can_be_joined_with_a_later_one()
    {
        var first = new SourceSpan(2, 5, 1, 3);
        var last = new SourceSpan(8, 11, 1, 9);

        first.Length.ShouldBe(3);
        first.To(last).ShouldBe(new SourceSpan(2, 11, 1, 3));
        new SourceSpan(4, 4, 1, 5).Length.ShouldBe(0);
    }
}
