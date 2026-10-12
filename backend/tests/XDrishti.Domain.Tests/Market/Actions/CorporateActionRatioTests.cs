using XDrishti.Domain.Market.Actions;

namespace XDrishti.Domain.Tests.Market.Actions;

/// <summary>What a ratio means and the price factor it gives (ADR 0017 §5): a split is old : new shares, a bonus is bonus : held shares.</summary>
public sealed class CorporateActionRatioTests
{
    [Theory]
    [InlineData(CorporateActionKind.Split, 1, 5, "0.2")]
    [InlineData(CorporateActionKind.Split, 1, 2, "0.5")]
    [InlineData(CorporateActionKind.Split, 2, 3, "0.666666666667")]
    [InlineData(CorporateActionKind.Split, 5, 1, "5")]
    [InlineData(CorporateActionKind.Bonus, 1, 1, "0.5")]
    [InlineData(CorporateActionKind.Bonus, 1, 2, "0.666666666667")]
    [InlineData(CorporateActionKind.Bonus, 1, 3, "0.75")]
    [InlineData(CorporateActionKind.Bonus, 2, 1, "0.333333333333")]
    [InlineData(CorporateActionKind.Bonus, 3, 2, "0.4")]
    public void The_factor_follows_the_meaning_of_the_ratio(CorporateActionKind kind, int numerator, int denominator, string expected)
    {
        CorporateActionRatio.Factor(kind, numerator, denominator).ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(CorporateActionKind.Split, 1, 1, false)]
    [InlineData(CorporateActionKind.Bonus, 1, 1, true)]
    [InlineData(CorporateActionKind.Split, 0, 5, false)]
    [InlineData(CorporateActionKind.Split, 1, 0, false)]
    [InlineData(CorporateActionKind.Bonus, -1, 2, false)]
    [InlineData(CorporateActionKind.Split, 1, CorporateActionRatio.MaxPart, true)]
    [InlineData(CorporateActionKind.Split, 1, CorporateActionRatio.MaxPart + 1, false)]
    public void Only_ratios_that_change_the_price_level_are_valid(CorporateActionKind kind, int numerator, int denominator, bool valid)
    {
        CorporateActionRatio.IsValid(kind, numerator, denominator).ShouldBe(valid);
    }

    [Fact]
    public void A_ratio_that_is_not_valid_has_no_factor()
    {
        Should.Throw<ArgumentException>(() => CorporateActionRatio.Factor(CorporateActionKind.Split, 3, 3));
    }

    [Fact]
    public void The_ratio_is_written_numerator_colon_denominator()
    {
        CorporateActionRatio.Text(1, 5).ShouldBe("1:5");
    }
}
