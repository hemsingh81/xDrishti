using XDrishti.Domain.Assistant.Proposals;

namespace XDrishti.Domain.Tests.Assistant.Proposals;

/// <summary>Spec P9-08 AC 4, D3: a proposed parameter keeps the baseline's sign, differs, moves at most 50 percent and has at most six significant digits.</summary>
public sealed class KnobBoundsTests
{
    [Theory]
    [InlineData("2.0", "2.5", null)]
    [InlineData("2.0", "1.0", null)]
    [InlineData("2.0", "3.0", null)]
    [InlineData("-2.0", "-2.5", null)]
    [InlineData("2.0", "3.1", KnobBounds.MoveTooLarge)]
    [InlineData("2.0", "0.99", KnobBounds.MoveTooLarge)]
    [InlineData("2.0", "-0.5", KnobBounds.SignChange)]
    [InlineData("2.0", "0", KnobBounds.SignChange)]
    [InlineData("2.0", "2.0", KnobBounds.Unchanged)]
    [InlineData("2.0", "2.00", KnobBounds.Unchanged)]
    [InlineData("0", "1", KnobBounds.BaselineZero)]
    [InlineData("2.0", "abc", KnobBounds.NotNumber)]
    [InlineData("2.0", "", KnobBounds.NotNumber)]
    [InlineData("2.0", "NaN", KnobBounds.NotNumber)]
    [InlineData("2.0", "1e999", KnobBounds.NotNumber)]
    public void The_candidate_is_checked_against_the_baseline(string baseline, string candidate, string? code)
    {
        var problems = KnobBounds.Problems(baseline, candidate);

        if (code is null)
        {
            problems.ShouldBeEmpty();
        }
        else
        {
            problems.ShouldContain(code);
        }
    }

    [Fact]
    public void Fifty_percent_is_allowed_exactly_and_a_hair_more_is_not()
    {
        KnobBounds.Problems("100", "150").ShouldBeEmpty();
        KnobBounds.Problems("100", "150.01").ShouldContain(KnobBounds.MoveTooLarge);
        KnobBounds.Problems("100", "50").ShouldBeEmpty();
        KnobBounds.Problems("100", "49.99").ShouldContain(KnobBounds.MoveTooLarge);
    }

    [Fact]
    public void The_limit_cannot_be_raised_above_fifty_percent()
    {
        KnobBounds.Problems("100", "160", maxMovePct: 90).ShouldContain(KnobBounds.MoveTooLarge);
        KnobBounds.Problems("100", "120", maxMovePct: 10).ShouldContain(KnobBounds.MoveTooLarge);
    }

    [Theory]
    [InlineData("1000", "1234.56", null)]
    [InlineData("1000", "1234.567", KnobBounds.TooManyDigits)]
    [InlineData("1000000", "1200000", null)]
    [InlineData("1000000", "1234567", KnobBounds.TooManyDigits)]
    public void At_most_six_significant_digits(string baseline, string candidate, string? code)
    {
        var problems = KnobBounds.Problems(baseline, candidate);

        if (code is null)
        {
            problems.ShouldBeEmpty();
        }
        else
        {
            problems.ShouldContain(code);
        }
    }

    [Fact]
    public void A_number_has_one_normal_form() => KnobBounds.Normal("2.50").ShouldBe(KnobBounds.Normal("2.5"));
}
