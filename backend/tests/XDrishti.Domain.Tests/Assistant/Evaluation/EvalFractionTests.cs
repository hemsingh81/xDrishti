namespace XDrishti.Domain.Tests.Assistant.Evaluation;

public sealed class EvalFractionTests
{
    [Theory]
    [InlineData("275/6", 2, 45.83)]
    [InlineData("275/6", 1, 45.8)]
    [InlineData("1/6", 4, 0.1667)]
    [InlineData("1/6", 2, 0.17)]
    [InlineData("17/13", 4, 1.3077)]
    [InlineData("-1/2", 0, -1)]
    [InlineData("1/2", 0, 1)]
    [InlineData("-5/2", 0, -3)]
    [InlineData("2.675", 2, 2.68)]
    [InlineData("4", 3, 4)]
    public void Rounds_half_away_from_zero_exactly(string text, int decimals, double expected) =>
        EvalFraction.Parse(text)!.Value.Round(decimals).ShouldBe((decimal)expected);

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("1/0")]
    [InlineData("x")]
    public void A_text_that_is_not_a_number_is_null(string text) => EvalFraction.Parse(text).ShouldBeNull();

    [Fact]
    public void Equal_fractions_are_equal_whatever_they_were_written_as()
    {
        EvalFraction.Parse("2/4").ShouldBe(EvalFraction.Parse("0.5"));
        EvalFraction.Parse("22/11").ShouldBe(EvalFraction.Of(2));
        EvalFraction.Parse("-1/2")!.Value.ToString().ShouldBe("-1/2");
        EvalFraction.Parse("275/6")!.Value.ToDouble().ShouldBe(45.8333333333, 1e-9);
    }
}
