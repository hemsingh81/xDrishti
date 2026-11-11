using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using static XDrishti.Strategies.Tests.Support.Compiling;

namespace XDrishti.Strategies.Tests.Binding;

/// <summary>BK-09 criterion 9: the three features compile with their pre-registered parameters only, and every other value is an error that names the allowed ones (no free parameter, so no grid search by accident).</summary>
public sealed class RelativeStrengthBindingTests
{
    [Fact]
    public void The_text_of_the_spec_compiles_to_the_three_specs_with_the_basket_as_a_code()
    {
        var plan = Condition("mom_rank(\"Nifty 100\", 126, 21) >= 0.67 and sector_rs(63) <= 3 and rs_vs_index(20) > 0", Timeframe.D1);

        plan.Features.ShouldBe(
        [
            new FeatureSpec("mom_rank", Timeframe.M1, PriceSource.Close, 2, 126, 21),
            new FeatureSpec("sector_rs", Timeframe.M1, PriceSource.Close, 63),
            new FeatureSpec("rs_vs_index", Timeframe.M1, PriceSource.Close, 20),
        ]);
    }

    [Theory]
    [InlineData("mom_rank(\"Nifty 100\", 252, 21) >= 0.5")]
    [InlineData("mom_rank(\"nifty 50\", 126, 21) >= 0.5")]
    [InlineData("mom_rank(\"F&O stocks\", 126, 21) >= 0.5")]
    [InlineData("sector_rs(21) <= 3")]
    [InlineData("sector_rs(126) <= 3")]
    public void The_pre_registered_values_compile(string source)
    {
        Of(source, Timeframe.D1).IsSuccess.ShouldBeTrue(source);
    }

    [Theory]
    [InlineData("mom_rank(\"Nifty 500\", 126, 21) >= 0.5", "\"Nifty 100\"")]
    [InlineData("mom_rank(2, 126, 21) >= 0.5", "name of a basket")]
    [InlineData("mom_rank(\"Nifty 100\", 100, 21) >= 0.5", "126, 252")]
    [InlineData("mom_rank(\"Nifty 100\", 126, 5) >= 0.5", "one of 21")]
    [InlineData("rs_vs_index(10) > 0", "one of 20")]
    [InlineData("sector_rs(30) <= 3", "21, 63, 126")]
    public void Any_other_value_is_one_error_that_names_the_allowed_ones(string source, string allowed)
    {
        var result = Of(source, Timeframe.D1);

        result.IsSuccess.ShouldBeFalse(source);
        result.Errors.ShouldHaveSingleItem().Message.ShouldContain(allowed);
    }
}
