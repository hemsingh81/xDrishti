using XDrishti.Strategies.Syntax;
using static XDrishti.Strategies.Tests.Support.Ast;

namespace XDrishti.Strategies.Tests.Syntax;

/// <summary>The canonical text, written by hand: one space around infix operators, the fewest parentheses that keep the meaning, numbers in their shortest form.</summary>
public sealed class ExpressionPrinterTests
{
    [Theory]
    [InlineData("rsi(14)>55", "rsi(14) > 55")]
    [InlineData("  rsi( 14 )\n>\t55  ", "rsi(14) > 55")]
    [InlineData("f(a,b ,c)", "f(a, b, c)")]
    [InlineData("f( )", "f()")]
    [InlineData("tf ( \"1h\" ) . ema ( 20 )", "tf(\"1h\").ema(20)")]
    [InlineData("tf(\"1h\").close", "tf(\"1h\").close")]
    [InlineData("prev_day . high", "prev_day.high")]
    [InlineData("ema(20,source = high)", "ema(20, source=high)")]
    [InlineData("close [ 1 ]", "close[1]")]
    [InlineData("close[-1]", "close[-1]")]
    [InlineData("tf(\"1h\").close[2]", "tf(\"1h\").close[2]")]
    [InlineData("((a))", "a")]
    [InlineData("(a)", "a")]
    [InlineData("(a + b) * c", "(a + b) * c")]
    [InlineData("a + (b * c)", "a + b * c")]
    [InlineData("(a * b) + c", "a * b + c")]
    [InlineData("(a - b) - c", "a - b - c")]
    [InlineData("a - (b - c)", "a - (b - c)")]
    [InlineData("(a / b) * c", "a / b * c")]
    [InlineData("a / (b * c)", "a / (b * c)")]
    [InlineData("(a and b) and c", "a and b and c")]
    [InlineData("a and (b and c)", "a and (b and c)")]
    [InlineData("(a or b) or c", "a or b or c")]
    [InlineData("a or (b or c)", "a or (b or c)")]
    [InlineData("(a and b) or c", "a and b or c")]
    [InlineData("a or (b and c)", "a or b and c")]
    [InlineData("(a or b) and c", "(a or b) and c")]
    [InlineData("a and (b or c)", "a and (b or c)")]
    [InlineData("(a < b) and (c > d)", "a < b and c > d")]
    [InlineData("(a < b) == (c < d)", "(a < b) == (c < d)")]
    [InlineData("a < (b + c)", "a < b + c")]
    [InlineData("(a + b) < c", "a + b < c")]
    [InlineData("not (a < b)", "not a < b")]
    [InlineData("(not a) < b", "(not a) < b")]
    [InlineData("a < (not b)", "a < (not b)")]
    [InlineData("not (a and b)", "not (a and b)")]
    [InlineData("not (a or b)", "not (a or b)")]
    [InlineData("(not a) and b", "not a and b")]
    [InlineData("a and (not b)", "a and not b")]
    [InlineData("not (not a)", "not not a")]
    [InlineData("-(-a)", "-(-a)")]
    [InlineData("- -a", "-(-a)")]
    [InlineData("(-a) * b", "-a * b")]
    [InlineData("-(a * b)", "-(a * b)")]
    [InlineData("-(a + b)", "-(a + b)")]
    [InlineData("a * (-b)", "a * -b")]
    [InlineData("a - (-b)", "a - -b")]
    [InlineData("-(not a)", "-(not a)")]
    [InlineData("-x[1]", "-x[1]")]
    [InlineData("-(x[1])", "-x[1]")]
    public void The_canonical_text_of_a_source_is_the_hand_written_one(string source, string canonical)
    {
        var result = ConditionParser.Parse(source);

        result.Problems.ShouldBeEmpty();
        ExpressionPrinter.Print(result.Tree!).ShouldBe(canonical);
    }

    [Theory]
    [InlineData("55", "55")]
    [InlineData("0", "0")]
    [InlineData("0.5", "0.5")]
    [InlineData("2.50", "2.5")]
    [InlineData("007", "7")]
    [InlineData("1e-3", "0.001")]
    [InlineData("1e-4", "0.0001")]
    [InlineData("1e-5", "0.00001")]
    [InlineData("1e-7", "1e-7")]
    [InlineData("0.0000001", "1e-7")]
    [InlineData("1e3", "1000")]
    [InlineData("1e-6", "0.000001")]
    [InlineData("14e-7", "0.0000014")]
    [InlineData("1e14", "100000000000000")]
    [InlineData("1e15", "1000000000000000")]
    [InlineData("1e20", "100000000000000000000")]
    [InlineData("1e21", "1e21")]
    [InlineData("15e20", "1.5e21")]
    [InlineData("12345678901234567890", "12345678901234567000")]
    [InlineData("123456789.125", "123456789.125")]
    [InlineData("1.5e-9", "1.5e-9")]
    [InlineData("1.7976931348623157e308", "1.7976931348623157e308")]
    [InlineData("5e-324", "5e-324")]
    [InlineData("0.1", "0.1")]
    [InlineData("3.14159", "3.14159")]
    [InlineData("1e0", "1")]
    [InlineData("100", "100")]
    public void A_number_is_written_in_its_shortest_form_that_reads_back_as_the_same_number(string source, string canonical)
    {
        var tree = ConditionParser.Parse(source).Tree!;

        ExpressionPrinter.Print(tree).ShouldBe(canonical);
        ConditionParser.Parse(canonical).Tree.ShouldBe(tree);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"09:45\"")]
    [InlineData("\"a b\"")]
    [InlineData("\"it's\"")]
    [InlineData("\"back\\slash\"")]
    [InlineData("\"tab\there\"")]
    [InlineData("\"भारत 😀\"")]
    public void A_string_is_written_between_double_quotes_as_it_is(string source)
    {
        ExpressionPrinter.Print(ConditionParser.Parse(source).Tree!).ShouldBe(source);
    }

    [Fact]
    public void Two_sources_that_differ_only_in_white_space_print_the_same()
    {
        string[][] groups =
        [
            ["rsi(14)>55", "rsi(14) > 55", "rsi( 14 )\n>\t55", "  rsi ( 14 ) > 55  ", "rsi\r\n(\r\n14\r\n)\r\n>\r\n55"],
            ["tf(\"1h\").ema(20)>tf(\"1h\").ema(50)", "tf ( \"1h\" ) . ema ( 20 ) > tf ( \"1h\" ) . ema ( 50 )", "tf(\"1h\")\n.ema(20)\n>\ntf(\"1h\")\n.ema(50)"],
            ["a and b or not c", "a  and  b  or  not  c", "a\nand\nb\nor\nnot\nc"],
            ["ema(20,source=high)", "ema(20 , source = high)", "ema(\n20,\n source\n=\n high\n)"],
            ["close[1]", "close [1]", "close[ 1 ]", "close\t[\t1\t]"],
            ["-x*y", "- x * y", "-\nx\n*\ny"],
        ];

        foreach (var group in groups)
        {
            var printed = group.Select(source => ExpressionPrinter.Print(ConditionParser.Parse(source).Tree!)).Distinct().ToList();

            printed.Count.ShouldBe(1, string.Join(" | ", printed));
        }
    }

    [Fact]
    public void Redundant_parentheses_do_not_change_the_canonical_text()
    {
        ExpressionPrinter.Print(ConditionParser.Parse("(((rsi(14))) > ((55)))").Tree!).ShouldBe("rsi(14) > 55");
    }

    [Fact]
    public void A_tree_built_by_hand_prints_like_the_parsed_one()
    {
        var tree = Or(And(Gt(C("rsi", N(14)), N(55)), Lt(R("close"), TfC("1h", "ema", N(20)))), Not(Gt(Mul(R("a"), Add(R("b"), N(1))), N(2))));

        ExpressionPrinter.Print(tree).ShouldBe("rsi(14) > 55 and close < tf(\"1h\").ema(20) or not a * (b + 1) > 2");
        ConditionParser.Parse(ExpressionPrinter.Print(tree)).Tree.ShouldBe(tree);
    }

    [Fact]
    public void A_string_that_cannot_be_written_is_refused()
    {
        Should.Throw<ArgumentException>(() => ExpressionPrinter.Print(S("say \"hi\"")));
        Should.Throw<ArgumentException>(() => ExpressionPrinter.Print(S("two\nlines")));
        Should.Throw<ArgumentException>(() => ExpressionPrinter.Print(S("two\rlines")));
        Should.Throw<ArgumentException>(() => ExpressionPrinter.Print(Tf("say \"hi\"", "close")));
    }

    [Fact]
    public void A_reference_with_both_a_scope_and_a_timeframe_cannot_be_written()
    {
        var both = new Reference(Id("high"), default, Id("prev_day"), S("1h"));

        Should.Throw<ArgumentException>(() => ExpressionPrinter.Print(both));
    }

    [Fact]
    public void A_number_that_is_not_finite_cannot_be_written()
    {
        Should.Throw<ArgumentException>(() => ExpressionPrinter.Print(N(double.NaN)));
        Should.Throw<ArgumentException>(() => ExpressionPrinter.Print(N(double.PositiveInfinity)));
    }

    [Fact]
    public void A_missing_expression_is_a_programming_error()
    {
        Should.Throw<ArgumentNullException>(() => ExpressionPrinter.Print(null!));
    }

    [Fact]
    public void A_call_made_by_hand_without_its_argument_list_prints_as_an_empty_call()
    {
        var call = new Call(Id("regime"), default, default);

        ExpressionPrinter.Print(call).ShouldBe("regime()");
    }
}
