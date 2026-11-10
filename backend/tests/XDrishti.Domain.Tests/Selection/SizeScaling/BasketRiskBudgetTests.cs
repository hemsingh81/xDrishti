using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Domain.Tests.Selection.SizeScaling;

/// <summary>BK-10 criterion 5 by hand (total limit 3.0 percent = 30,000, basket budget 40 percent = 12,000) and the property that a budget is never exceeded.</summary>
public sealed class BasketRiskBudgetTests
{
    // Percent of capital: limit 3.0, candidate 0.5 (5,000 of 1,000,000), used 0.9 (9,000).
    [Fact]
    public void Room_of_three_thousand_scales_a_five_thousand_candidate_to_sixty_percent_using_all_of_the_budget()
    {
        var fit = BasketRiskBudget.Fit(40, 3.0, 0.9, 0.5, 0.5);

        fit.Outcome.ShouldBe(BudgetFitOutcome.Scaled);
        fit.Room.ShouldBe(0.3, 1e-9);
        fit.Ratio.ShouldBe(0.6, 1e-9);
        fit.UsesPctOfBudget!.Value.ShouldBe(100.0, 1e-6);
        fit.Text.ShouldBe("uses 100 % of basket budget");
    }

    [Fact]
    public void Room_of_two_thousand_gives_a_ratio_below_the_floor_so_the_candidate_goes_to_the_reserve_with_the_numbers()
    {
        var fit = BasketRiskBudget.Fit(40, 3.0, 1.0, 0.5, 0.5);

        fit.Outcome.ShouldBe(BudgetFitOutcome.Reserve);
        fit.Ratio.ShouldBe(0.0);
        fit.Text.ShouldContain("room 0.2");
        fit.Text.ShouldContain("of 1.2");
        fit.Text.ShouldContain("needs 0.5");
    }

    [Fact]
    public void A_candidate_inside_the_room_is_taken_whole()
    {
        var fit = BasketRiskBudget.Fit(40, 3.0, 0.2, 0.5, 0.5);

        fit.Outcome.ShouldBe(BudgetFitOutcome.Fits);
        fit.Ratio.ShouldBe(1.0);
        fit.UsesPctOfBudget!.Value.ShouldBe((0.2 + 0.5) / 1.2 * 100, 1e-9);
    }

    [Fact]
    public void A_full_basket_is_a_reserve_never_a_zero_sized_ticket()
    {
        BasketRiskBudget.Fit(40, 3.0, 1.2, 0.1, 0.5).Outcome.ShouldBe(BudgetFitOutcome.Reserve);
        BasketRiskBudget.Fit(40, 3.0, 2.0, 0.1, 0.5).Outcome.ShouldBe(BudgetFitOutcome.Reserve);
    }

    [Fact]
    public void Property_the_ratio_is_never_above_one_and_the_budget_is_never_exceeded()
    {
        var random = new Random(5);
        for (var i = 0; i < 20_000; i++)
        {
            var pct = 1 + (random.NextDouble() * 99);
            var limit = random.NextDouble() * 5;
            var used = random.NextDouble() * 3;
            var risk = random.NextDouble() * 1.5;
            var floor = 0.25 + (random.NextDouble() * 0.75);

            var fit = BasketRiskBudget.Fit(pct, limit, used, risk, floor);

            fit.Ratio.ShouldBeLessThanOrEqualTo(1.0);
            fit.Ratio.ShouldBeGreaterThanOrEqualTo(0.0);
            if (fit.Outcome != BudgetFitOutcome.Reserve && used <= fit.Budget)
            {
                (used + (risk * fit.Ratio)).ShouldBeLessThanOrEqualTo(fit.Budget + 1e-9);
            }

            if (fit.Outcome == BudgetFitOutcome.Scaled)
            {
                fit.Ratio.ShouldBeGreaterThanOrEqualTo(floor - 1e-12);
            }
        }
    }
}
