using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Actions.Rules;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Actions.ActionFixtures;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>Long-term soon (acceptance criterion 4): a profitable open lot that is 12 months old within the next 30 days (calendar days, India date), one item per holding, informational. Today is Monday 2026-10-12 unless a test says otherwise.</summary>
public sealed class LongTermSoonRuleTests
{
    private static HoldingData Held(DateOnly acquired, decimal price = 150m, decimal cost = 100m) => Pos("ABC", price, 10, cost, acquired);

    [Theory]
    [InlineData("2025-11-11", true)] // 12 months on 2026-11-11: 30 days ahead, the last day of the window
    [InlineData("2025-11-12", false)] // 31 days ahead
    [InlineData("2025-10-13", true)] // tomorrow
    [InlineData("2025-10-12", false)] // 12 months old today: already long-term, not "soon"
    [InlineData("2025-10-01", false)]
    [InlineData("2026-03-01", false)]
    public void The_lot_must_turn_12_months_old_in_the_next_30_days(string acquired, bool fires)
    {
        var hits = LongTermSoonRule.Evaluate(Context([Held(DateOnly.Parse(acquired, System.Globalization.CultureInfo.InvariantCulture))]));

        hits.Count.ShouldBe(fires ? 1 : 0);
    }

    [Theory]
    [InlineData("2024-02-29", "2025-02-10", true)] // a leap day turns 12 months old on 28 February: 18 days ahead
    [InlineData("2024-02-29", "2025-02-28", false)] // it is 12 months old on that day already
    [InlineData("2024-02-29", "2025-03-01", false)]
    [InlineData("2023-03-01", "2024-02-10", true)] // 12 months on 2024-03-01, 20 days ahead across a leap day
    public void Leap_days_are_calendar_arithmetic(string acquired, string today, bool fires)
    {
        var hits = LongTermSoonRule.Evaluate(Context([Held(DateOnly.Parse(acquired, System.Globalization.CultureInfo.InvariantCulture))], today: DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture)));

        hits.Count.ShouldBe(fires ? 1 : 0);
    }

    [Fact]
    public void A_lot_without_a_gain_does_not_count()
    {
        LongTermSoonRule.Evaluate(Context([Held(new DateOnly(2025, 11, 1), price: 90m)])).ShouldBeEmpty();
        LongTermSoonRule.Evaluate(Context([Held(new DateOnly(2025, 11, 1), price: 100m)])).ShouldBeEmpty();
    }

    [Fact]
    public void An_unpriced_holding_never_fires()
    {
        LongTermSoonRule.Evaluate(Context([Held(new DateOnly(2025, 11, 1), price: 0m)])).ShouldBeEmpty();
    }

    [Fact]
    public void Several_lots_make_one_item_listing_the_profitable_ones_by_date()
    {
        var holding = Pos("ABC", 150m).WithLots(
            HoldingFixtures.Open(5, 100m, new DateOnly(2025, 11, 10)),
            HoldingFixtures.Open(3, 200m, new DateOnly(2025, 11, 1)), // at a loss: left out
            HoldingFixtures.Open(2, 120m, new DateOnly(2025, 10, 20)),
            HoldingFixtures.Closed(4, 100m, new DateOnly(2025, 10, 15), new DateOnly(2026, 1, 1), 160m));

        var hit = LongTermSoonRule.Evaluate(Context([holding])).ShouldHaveSingleItem();

        hit.Item.Numbers.Lots!.Select(l => (l.AcquiredOn, l.Qty, l.LongTermOn, l.DaysLeft, l.GainPct)).ShouldBe(
        [
            (new DateOnly(2025, 10, 20), 2L, new DateOnly(2026, 10, 20), 8, 25m),
            (new DateOnly(2025, 11, 10), 5L, new DateOnly(2026, 11, 10), 29, 50m),
        ]);
        hit.Item.Severity.ShouldBe("info");
        hit.Item.Detail.ShouldBe("2 profitable lots turn 12 months old within 30 days (the first on 20 Oct 2026). Check the tax impact before selling early.");
    }

    [Fact]
    public void The_window_is_a_threshold()
    {
        var held = Held(new DateOnly(2025, 12, 1)); // 12 months on 2026-12-01: 50 days ahead

        LongTermSoonRule.Evaluate(Context([held])).ShouldBeEmpty();
        LongTermSoonRule.Evaluate(Context([held], ActionThresholds.Defaults with { LongTermWindowDays = 60 })).ShouldHaveSingleItem();
    }

    [Fact]
    public void The_date_is_the_one_in_the_context_so_the_india_date_decides()
    {
        var held = Held(new DateOnly(2025, 11, 12));

        LongTermSoonRule.Evaluate(Context([held], today: new DateOnly(2026, 10, 12))).ShouldBeEmpty();
        LongTermSoonRule.Evaluate(Context([held], today: new DateOnly(2026, 10, 13))).ShouldHaveSingleItem();
    }

    [Fact]
    public void A_lot_that_cost_nothing_is_profitable_and_listed_without_a_gain_percentage()
    {
        // bonus or transferred-in shares: unit cost 0 is allowed by the database
        var holding = Pos("BONUS", 150m).WithLots(
            HoldingFixtures.Open(5, 0m, new DateOnly(2025, 11, 1)),
            HoldingFixtures.Open(5, 100m, new DateOnly(2025, 11, 2)));

        var hit = LongTermSoonRule.Evaluate(Context([holding])).ShouldHaveSingleItem();

        hit.Item.Numbers.Lots!.Select(l => (l.Qty, l.GainPct)).ShouldBe([(5L, (decimal?)null), (5L, (decimal?)50m)]);
        hit.Item.Detail.ShouldStartWith("2 profitable lots turn 12 months old within 30 days");
    }

    [Fact]
    public void No_rule_divides_by_a_cost_of_zero()
    {
        var free = Pos("FREE", 150m).WithLots(HoldingFixtures.Open(10, 0m, new DateOnly(2025, 11, 1)));
        var context = Context([free]);

        BelowCostRule.Evaluate(context).ShouldBeEmpty();
        LargeGainRule.Evaluate(context).ShouldBeEmpty(); // no cost, no percentage
        OverweightRule.Evaluate(context).ShouldBeEmpty();
        LaggingNiftyRule.Evaluate(context, null, null).Hits.ShouldBeEmpty();
        NotInBasketRule.Evaluate(context).ShouldHaveSingleItem().Item.Numbers.Invested.ShouldBe(0m);
        ActionRuleSet.Evaluate(context, null, null).Hits.Select(h => h.Item.Rule).ShouldBe(["long_term_soon", "not_in_basket"]);
    }

    [Fact]
    public void A_holding_of_only_free_shares_held_long_is_unavailable_for_the_lagging_rule_not_an_error()
    {
        var free = Pos("FREE", 150m, acquired: new DateOnly(2025, 1, 2)).WithLots(HoldingFixtures.Open(10, 0m, new DateOnly(2025, 1, 2)));

        var result = LaggingNiftyRule.Evaluate(Context([free]), Index((new DateOnly(2025, 1, 2), 1000m), (Today, 1300m)), XDrishti.Application.Tests.Portfolio.History.HistoryData.Weekdays);

        (result.Hits.Count, result.Unavailable.Count).ShouldBe((0, 1));
    }
}
