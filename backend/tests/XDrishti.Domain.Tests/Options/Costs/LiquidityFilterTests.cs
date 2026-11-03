using XDrishti.Domain.Options.Costs;

namespace XDrishti.Domain.Tests.Options.Costs;

/// <summary>P14-07 criterion 5: the rupee liquidity filter on the hand cases of the spec. The thresholds are the UNVERIFIED starting values of the settings, passed in.</summary>
public sealed class LiquidityFilterTests
{
    private static readonly LiquidityThresholds Start = new(5m, 25m);

    [Fact]
    public void An_open_interest_of_2_2_crore_is_filtered_and_one_of_11_crore_passes()
    {
        var thin = LiquidityFilter.Evaluate(1_000, 1_000, 22_000m, Start);
        var deep = LiquidityFilter.Evaluate(5_000, 1_000, 22_000m, Start);

        thin.Verdict.ShouldBe(LiquidityVerdict.Filtered);
        thin.OiNotionalRupees.ShouldBe(22_000_000m); // 2.2 crore
        deep.Verdict.ShouldBe(LiquidityVerdict.Pass);
        deep.OiNotionalRupees.ShouldBe(110_000_000m); // 11 crore
        deep.BarTurnoverRupees.ShouldBe(22_000_000m);
        deep.Code.ShouldBe("pass");
    }

    [Fact]
    public void The_bar_turnover_must_also_reach_its_threshold()
    {
        var quietBar = LiquidityFilter.Evaluate(5_000, 100, 22_000m, Start); // 100 x 22,000 = 22 lakh < 25 lakh
        var exactBar = LiquidityFilter.Evaluate(5_000, 125, 20_000m, Start); // 25 lakh
        var exactOi = LiquidityFilter.Evaluate(2_500, 1_000, 20_000m, Start); // 5 crore

        quietBar.Verdict.ShouldBe(LiquidityVerdict.Filtered);
        exactBar.Verdict.ShouldBe(LiquidityVerdict.Pass);
        exactOi.Verdict.ShouldBe(LiquidityVerdict.Pass);
    }

    [Fact]
    public void A_null_open_interest_or_volume_is_liquidity_unknown_and_never_a_pass()
    {
        foreach (var result in new[]
        {
            LiquidityFilter.Evaluate(null, 1_000_000, 22_000m, Start),
            LiquidityFilter.Evaluate(1_000_000, null, 22_000m, Start),
            LiquidityFilter.Evaluate(null, null, 22_000m, Start),
            LiquidityFilter.Evaluate(1_000_000, 1_000_000, 0m, Start),
        })
        {
            result.Verdict.ShouldBe(LiquidityVerdict.Unknown);
            result.Code.ShouldBe("liquidity_unknown");
            result.OiNotionalRupees.ShouldBeNull();
        }

        default(LiquidityVerdict).ShouldBe(LiquidityVerdict.Unknown);
    }
}
