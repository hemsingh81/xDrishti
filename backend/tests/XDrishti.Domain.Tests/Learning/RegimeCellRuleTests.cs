using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10 part 10d-1, D9 and AC14 (ADR 0220): a regime cell shrinks to its parent below 30 trades and stands alone at 30. Regimes (P6-02) exist, and the 30 is the bar their breakdown already uses.</summary>
public sealed class RegimeCellRuleTests
{
    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(31, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void A_regime_cell_with_29_trades_shrinks_to_its_parent_and_with_30_it_stands_alone(int trades, bool alone)
    {
        RegimeCellRule.StandsAlone(trades).ShouldBe(alone);
        RegimeCellRule.ShrinksToParent(trades).ShouldBe(!alone);
    }

    [Fact]
    public void Four_regimes_of_60_trades_make_cells_of_15_which_all_shrink_to_the_parent()
    {
        var perRegime = 60 / 4;

        perRegime.ShouldBe(15);
        RegimeCellRule.ShrinksToParent(perRegime).ShouldBeTrue();
    }

    [Fact]
    public void The_bar_is_the_one_the_regime_breakdown_marks_as_enough_evidence()
    {
        RegimeCellRule.StandAloneTrades.ShouldBe(30);
        RegimeBreakdown.MinTrades.ShouldBe(RegimeCellRule.StandAloneTrades);
    }
}
