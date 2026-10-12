using XDrishti.Domain.Market.Actions;

namespace XDrishti.Domain.Tests.Market.Actions;

/// <summary>The cumulative backward adjustment: bars before an ex-date are multiplied, the ex-date itself and later are not; volume is divided; rounding is only at the output.</summary>
public sealed class AdjustmentFactorsTests
{
    private static readonly DateOnly Ex1 = new(2025, 3, 10);

    private static readonly DateOnly Ex2 = new(2026, 6, 1);

    private static AppliedAction Split(DateOnly exDate, int numerator, int denominator) =>
        new(exDate, CorporateActionKind.Split, numerator, denominator, CorporateActionRatio.Factor(CorporateActionKind.Split, numerator, denominator));

    private static AppliedAction Bonus(DateOnly exDate, int numerator, int denominator) =>
        new(exDate, CorporateActionKind.Bonus, numerator, denominator, CorporateActionRatio.Factor(CorporateActionKind.Bonus, numerator, denominator));

    [Fact]
    public void Without_actions_every_bar_keeps_its_prices()
    {
        AdjustmentFactors.None.FactorAt(new DateOnly(2020, 1, 1)).ShouldBe(1.0);
        AdjustmentFactors.From([]).Actions.ShouldBeEmpty();
    }

    [Fact]
    public void A_bar_strictly_before_the_ex_date_is_multiplied_and_the_ex_date_itself_is_not()
    {
        var factors = AdjustmentFactors.From([Split(Ex1, 1, 5)]);

        factors.FactorAt(Ex1.AddDays(-1)).ShouldBe(0.2);
        factors.FactorAt(Ex1).ShouldBe(1.0);
        factors.FactorAt(Ex1.AddDays(1)).ShouldBe(1.0);
    }

    [Fact]
    public void Two_actions_multiply_for_the_bars_before_both_and_only_the_later_one_applies_between_them()
    {
        var factors = AdjustmentFactors.From([Split(Ex2, 1, 2), Split(Ex1, 1, 5)]);

        factors.FactorAt(Ex1.AddDays(-1)).ShouldBe(0.1, 1e-12);
        factors.FactorAt(Ex1).ShouldBe(0.5);
        factors.FactorAt(Ex2.AddDays(-1)).ShouldBe(0.5);
        factors.FactorAt(Ex2).ShouldBe(1.0);
        factors.Actions.Select(a => a.ExDate).ShouldBe([Ex1, Ex2]);
    }

    [Fact]
    public void The_order_in_which_the_actions_are_given_does_not_matter()
    {
        var a = AdjustmentFactors.From([Split(Ex1, 1, 5), Bonus(Ex2, 1, 1)]);
        var b = AdjustmentFactors.From([Bonus(Ex2, 1, 1), Split(Ex1, 1, 5)]);

        a.FactorAt(Ex1.AddDays(-10)).ShouldBe(b.FactorAt(Ex1.AddDays(-10)));
    }

    [Fact]
    public void A_bonus_one_for_one_halves_the_price_and_one_for_two_takes_a_third()
    {
        AdjustmentFactors.From([Bonus(Ex1, 1, 1)]).FactorAt(Ex1.AddDays(-1)).ShouldBe(0.5);
        AdjustmentFactors.From([Bonus(Ex1, 1, 2)]).FactorAt(Ex1.AddDays(-1)).ShouldBe(2.0 / 3, 1e-9);
        AdjustmentFactors.From([Bonus(Ex1, 3, 2)]).FactorAt(Ex1.AddDays(-1)).ShouldBe(0.4);
    }

    [Fact]
    public void A_split_and_a_bonus_on_the_same_day_both_apply()
    {
        var factors = AdjustmentFactors.From([Split(Ex1, 1, 2), Bonus(Ex1, 1, 1)]);

        factors.FactorAt(Ex1.AddDays(-1)).ShouldBe(0.25);
        factors.ActionsAfter(Ex1.AddDays(-1)).Count.ShouldBe(2);
    }

    [Fact]
    public void The_actions_after_a_date_are_the_ones_that_change_bars_from_that_date()
    {
        var factors = AdjustmentFactors.From([Split(Ex1, 1, 5), Split(Ex2, 1, 2)]);

        factors.ActionsAfter(Ex1.AddDays(-1)).Select(a => a.ExDate).ShouldBe([Ex1, Ex2]);
        factors.ActionsAfter(Ex1).Select(a => a.ExDate).ShouldBe([Ex2]);
        factors.ActionsAfter(Ex2).ShouldBeEmpty();
    }

    [Fact]
    public void Prices_are_rounded_to_four_decimals_and_volume_is_divided_by_the_factor()
    {
        AdjustmentFactors.Price(1000.5, 0.2).ShouldBe(200.1);
        AdjustmentFactors.Price(333.333333, 1.0 / 3).ShouldBe(111.1111);
        AdjustmentFactors.Volume(100_000, 0.2).ShouldBe(500_000);
        AdjustmentFactors.Volume(1, 0.4).ShouldBe(3); // 2.5 rounds away from zero
        AdjustmentFactors.Volume(7, 1.0).ShouldBe(7);
    }

    [Fact]
    public void Rounding_happens_only_at_the_output_so_chained_factors_do_not_drift()
    {
        var factors = AdjustmentFactors.From([Split(Ex1, 1, 3), Split(Ex2, 1, 3)]);

        AdjustmentFactors.Price(900, factors.FactorAt(Ex1.AddDays(-1))).ShouldBe(100.0);
    }
}
