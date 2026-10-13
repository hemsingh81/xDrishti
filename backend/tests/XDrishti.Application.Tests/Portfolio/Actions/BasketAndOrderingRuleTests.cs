using XDrishti.Application.Portfolio.Actions;
using XDrishti.Application.Portfolio.Actions.Rules;
using static XDrishti.Application.Tests.Portfolio.Actions.ActionFixtures;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>Not in a basket (acceptance criterion 6) and the order of the items.</summary>
public sealed class BasketAndOrderingRuleTests
{
    [Fact]
    public void A_mapped_open_holding_in_no_basket_fires_as_info_and_one_in_a_basket_does_not()
    {
        var hits = NotInBasketRule.Evaluate(Context([Pos("IN", 100m), Pos("OUT", 100m)], inBasket: ["IN"]));

        var hit = hits.ShouldHaveSingleItem();
        (hit.Item.Symbol, hit.Item.Severity, hit.Item.Rule, hit.Item.Detail).ShouldBe(("OUT", "info", "not_in_basket", "This holding is not in any basket yet."));
        hit.Item.Suggestions.ShouldBe(["open", "set_alert", "add_to_basket", "snooze", "done"]);
    }

    [Fact]
    public void A_holding_that_is_not_in_the_master_cannot_be_put_in_a_basket_so_it_never_fires()
    {
        NotInBasketRule.Evaluate(Context([Pos("X", 100m, mapped: false)])).ShouldBeEmpty();
    }

    [Fact]
    public void An_unpriced_holding_can_fire_because_the_rule_is_not_price_based()
    {
        var hit = NotInBasketRule.Evaluate(Context([Pos("NEW", 0m)])).ShouldHaveSingleItem();

        (hit.Item.Numbers.Value, hit.Item.Numbers.Invested).ShouldBe((null, 1000m));
    }

    [Fact]
    public void A_closed_holding_does_not_fire()
    {
        NotInBasketRule.Evaluate(Context([Pos("GONE", 100m) with { Status = "closed" }])).ShouldBeEmpty();
    }

    [Fact]
    public void Items_are_ordered_by_severity_then_by_the_size_of_the_trigger_then_by_symbol()
    {
        var holdings = new[]
        {
            Pos("MILD", 88m),   // -12 %  medium
            Pos("DEEP", 60m),   // -40 %  high
            Pos("DEEPER", 50m), // -50 %  high
            Pos("UP", 140m),    // +40 %  info, and 41 % of the account: overweight, medium
        };

        var (hits, _) = ActionRuleSet.Evaluate(Context(holdings, inBasket: ["MILD", "DEEP", "DEEPER", "UP"]), null, null);

        hits.Select(h => (h.Item.Symbol, h.Item.Rule)).ShouldBe([("DEEPER", "below_cost"), ("DEEP", "below_cost"), ("MILD", "below_cost"), ("UP", "overweight"), ("UP", "large_gain")]);
    }

    [Fact]
    public void Ties_are_broken_by_symbol_and_then_by_id_so_the_order_is_the_same_on_every_read()
    {
        var holdings = new[] { Pos("B", 70m), Pos("A", 70m), Pos("C", 70m) };

        var (first, _) = ActionRuleSet.Evaluate(Context(holdings, inBasket: ["A", "B", "C"]), null, null);
        var (second, _) = ActionRuleSet.Evaluate(Context(holdings.Reverse(), inBasket: ["A", "B", "C"]), null, null);

        first.Select(h => h.Item.Symbol).ShouldBe(["A", "B", "C"]);
        second.Select(h => h.Item.Id).ShouldBe(first.Select(h => h.Item.Id));
    }

    [Fact]
    public void The_ids_are_stable_between_evaluations()
    {
        var holding = Pos("ABC", 70m);

        var (a, _) = ActionRuleSet.Evaluate(Context([holding], inBasket: ["ABC"]), null, null);
        var (b, _) = ActionRuleSet.Evaluate(Context([holding with { LastPrice = 60m }], inBasket: ["ABC"]), null, null);

        a.Single().Item.Id.ShouldBe(b.Single().Item.Id); // severity and numbers are recomputed, the id is not
        a.Single().Item.Id.ShouldBe($"below_cost:{holding.Id}");
    }

    [Fact]
    public void Rules_of_one_severity_are_ordered_by_the_rule_not_by_sizes_in_different_units()
    {
        // not_in_basket is sized in rupees (here 1,000,000), large_gain in percent (40) and long_term_soon in days: the rupees must not win.
        var holdings = new[]
        {
            Pos("RUPEES", 100m, qty: 10_000),                                       // not in a basket, 1,000,000 of value
            Pos("PERCENT", 140m, qty: 10),                                           // +40 %
            Pos("DAYS", 110m, qty: 10, acquired: new DateOnly(2025, 11, 1)),         // 12 months old in 20 days, +10 %
            Pos("BELOW", 85m, qty: 10),                                              // -15 %: medium
        };

        var (hits, _) = ActionRuleSet.Evaluate(Context(holdings, inBasket: ["PERCENT", "DAYS", "BELOW"]), null, null);

        hits.Select(h => (h.Item.Rule, h.Item.Symbol)).ShouldBe(
        [
            ("below_cost", "BELOW"),
            ("overweight", "RUPEES"),
            ("large_gain", "PERCENT"),
            ("long_term_soon", "DAYS"),
            ("not_in_basket", "RUPEES"),
        ]);
    }

    [Fact]
    public void The_priority_of_the_rules_is_a_permutation_of_all_the_rules()
    {
        ActionRuleNames.ByPriority.Order().ShouldBe(ActionRuleNames.All.Order());
    }
}
