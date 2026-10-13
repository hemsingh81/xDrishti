using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Actions.Rules;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Actions.ActionFixtures;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>Overweight (acceptance criterion 2): the weight in the account's priced value against max(15 %, 1.5 x the equal-weight share), for accounts with at least three priced positions. Positions are 10 shares, so a price of 300 is a value of 3,000.</summary>
public sealed class OverweightRuleTests
{
    [Fact]
    public void A_holding_above_one_and_a_half_times_the_equal_share_fires_with_its_weight_and_limit()
    {
        var hits = OverweightRule.Evaluate(Context([Pos("A", 400m), Pos("B", 200m), Pos("C", 200m), Pos("D", 200m)]));

        var hit = hits.ShouldHaveSingleItem();
        (hit.Item.Symbol, hit.Item.Severity, hit.Item.Rule).ShouldBe(("A", "medium", "overweight"));
        (hit.Item.Numbers.WeightPct, hit.Item.Numbers.ThresholdPct, hit.Item.Numbers.Positions).ShouldBe((40m, 37.5m, 4));
        hit.Item.Detail.ShouldBe("40.0 % of Account One's value, above the 37.5 % limit (4 positions)");
    }

    [Fact]
    public void A_weight_exactly_at_the_limit_does_not_fire_and_just_above_does()
    {
        // 3,000 of 8,000 is 37.5 %, the limit of four positions.
        Hits("A", 300m, 200m, 200m, 100m).ShouldBeEmpty();
        Hits("A", 301m, 200m, 200m, 100m).ShouldBe(["A"]);
    }

    [Fact]
    public void With_three_positions_the_limit_is_half_the_account()
    {
        Hits("A", 500m, 250m, 250m).ShouldBeEmpty();
        Hits("A", 600m, 200m, 200m).ShouldBe(["A"]);
    }

    [Fact]
    public void An_account_with_fewer_than_three_priced_positions_never_fires()
    {
        Hits("A", 900m, 100m).ShouldBeEmpty();
        Hits("A", 900m).ShouldBeEmpty();
    }

    [Fact]
    public void An_unpriced_holding_is_not_a_position()
    {
        var hits = OverweightRule.Evaluate(Context([Pos("A", 900m), Pos("B", 100m), Pos("C", 0m)]));

        hits.ShouldBeEmpty();
    }

    [Fact]
    public void With_many_positions_the_floor_of_15_percent_is_the_limit()
    {
        // 12 positions: 1.5 x 8.33 % = 12.5 %, below the floor. 1,600 of 10,400 is 15.38 %.
        var holdings = new List<HoldingData> { Pos("BIG", 160m) };
        holdings.AddRange(Enumerable.Range(1, 11).Select(i => Pos("S" + i, 80m)));

        var hit = OverweightRule.Evaluate(Context(holdings)).ShouldHaveSingleItem();

        (hit.Item.Symbol, hit.Item.Numbers.ThresholdPct, hit.Item.Numbers.Positions).ShouldBe(("BIG", 15m, 12));
    }

    [Fact]
    public void Accounts_are_judged_separately()
    {
        var holdings = new[]
        {
            Pos("A1", 100m), Pos("A2", 100m), Pos("A3", 100m),
            Pos("B1", 900m, account: AccountTwo), Pos("B2", 50m, account: AccountTwo), Pos("B3", 50m, account: AccountTwo),
        };

        var hits = OverweightRule.Evaluate(Context(holdings));

        hits.Select(h => h.Item.Symbol).ShouldBe(["B1"]);
    }

    [Fact]
    public void The_minimum_number_of_positions_and_the_floor_are_thresholds()
    {
        var thresholds = ActionThresholds.Defaults with { MinPositions = 2 };

        var hits = OverweightRule.Evaluate(Context([Pos("A", 800m), Pos("B", 200m)], thresholds));

        hits.Select(h => h.Item.Symbol).ShouldBe(["A"]);
    }

    [Fact]
    public void The_size_of_the_trigger_is_the_excess_over_the_limit()
    {
        var hit = OverweightRule.Evaluate(Context([Pos("A", 400m), Pos("B", 200m), Pos("C", 200m), Pos("D", 200m)])).ShouldHaveSingleItem();

        hit.Size.ShouldBe(2.5m);
    }

    private static string[] Hits(params object[] priceAndSymbol)
    {
        // ("A", 300m, 200m, ...): the first is the symbol of the heavy holding, the rest are the prices; the others are named B, C, ...
        var heavy = (string)priceAndSymbol[0];
        var prices = priceAndSymbol.Skip(1).Cast<decimal>().ToList();
        var holdings = prices.Select((p, i) => Pos(i == 0 ? heavy : ((char)('A' + i)).ToString(), p)).ToList();
        return [.. OverweightRule.Evaluate(Context(holdings)).Select(h => h.Item.Symbol!)];
    }
}
