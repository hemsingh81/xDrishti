using XDrishti.Domain.Assistant;

namespace XDrishti.Domain.Tests.Assistant;

/// <summary>Spec P9-05 AC 11: exact decimal arithmetic, away-from-zero rounding, and nothing but numbers, operators and four functions is accepted.</summary>
public sealed class CalcTests
{
    [Theory]
    [InlineData("0.1+0.2", "0.3")]
    [InlineData("1/3", "0.3333")]
    [InlineData("round(2.675, 2)", "2.68")]
    [InlineData("round(-2.675, 2)", "-2.68")]
    [InlineData("(0.8-0.6)*100", "20.0")]
    [InlineData("abs(-3)", "3")]
    [InlineData("min(2,5)", "2")]
    [InlineData("max(2,5,9)", "9")]
    [InlineData("-(2+3)*2", "-10")]
    [InlineData("2*-3", "-6")]
    [InlineData("10 / 4", "2.5")]
    [InlineData(".5+.25", "0.75")]
    [InlineData("round(7)", "7")]
    [InlineData("0.8-0.6", "0.2")]
    public void An_expression_gives_its_exact_decimal_text(string expression, string expected)
    {
        var result = Calc.Evaluate(expression);

        result.IsSuccess.ShouldBeTrue(result.Reason);
        result.Text.ShouldBe(expected);
    }

    [Fact]
    public void The_places_argument_sets_the_rounding()
    {
        Calc.Evaluate("1/3", 2).Text.ShouldBe("0.33");
        Calc.Evaluate("2/3", 0).Text.ShouldBe("1");
        Calc.Evaluate("1/3", 9).ErrorCode.ShouldBe("calc.rejected");
    }

    [Theory]
    [InlineData("1/0")]
    [InlineData("5/(2-2)")]
    [InlineData("1/(0.5-0.5)")]
    public void A_division_by_zero_is_its_own_code(string expression)
    {
        Calc.Evaluate(expression).ErrorCode.ShouldBe("calc.divide_by_zero");
    }

    [Theory]
    [InlineData("x+1")]
    [InlineData("pi*2")]
    [InlineData("2^3")]
    [InlineData("2**3")]
    [InlineData("'a'+1")]
    [InlineData("\"a\"")]
    [InlineData("1e5")]
    [InlineData("1.5.2")]
    [InlineData("sqrt(4)")]
    [InlineData("round")]
    [InlineData("abs(1,2)")]
    [InlineData("min(1)")]
    [InlineData("round(1.5, 1.5)")]
    [InlineData("round(1.5, 11)")]
    [InlineData("1+")]
    [InlineData("(1+2")]
    [InlineData("1 2")]
    [InlineData("")]
    [InlineData("1; 2")]
    [InlineData("1,000+1")]
    [InlineData("Math.Abs(1)")]
    [InlineData("System.Environment.Exit(1)")]
    public void Anything_else_is_rejected(string expression)
    {
        var result = Calc.Evaluate(expression);

        result.IsSuccess.ShouldBeFalse(expression);
        result.ErrorCode.ShouldBe("calc.rejected", expression);
    }

    [Fact]
    public void The_limits_are_fifty_tokens_and_two_hundred_characters()
    {
        Calc.Evaluate(string.Join("+", Enumerable.Repeat("1", 25))).Text.ShouldBe("25"); // 49 tokens
        Calc.Evaluate(string.Join("+", Enumerable.Repeat("1", 26))).ErrorCode.ShouldBe("calc.rejected"); // 51 tokens
        Calc.Evaluate(new string('1', 28)).Text.ShouldBe(new string('1', 28)); // the largest run of digits a decimal holds
    }

    [Fact]
    public void A_number_too_large_or_a_text_too_long_is_rejected_not_thrown()
    {
        Calc.Evaluate(new string('9', 40)).ErrorCode.ShouldBe("calc.rejected");
        Calc.Evaluate(new string('1', 201)).ErrorCode.ShouldBe("calc.rejected");
        Calc.Evaluate("79228162514264337593543950335*2").ErrorCode.ShouldBe("calc.rejected");
    }

    [Fact]
    public void Deep_nesting_is_rejected()
    {
        Calc.Evaluate(new string('(', 30) + "1" + new string(')', 30)).ErrorCode.ShouldBe("calc.rejected");
    }

    [Fact]
    public void The_result_echoes_the_number_literals_of_the_expression()
    {
        Calc.Evaluate("12.5/4+round(2.675, 2)").Operands.ShouldBe(["12.5", "4", "2.675", "2"]);
        Calc.Evaluate("0.90+0").Operands.ShouldBe(["0.90", "0"]);
        Calc.Evaluate("1/0").Operands.ShouldBeNull();
    }
}
