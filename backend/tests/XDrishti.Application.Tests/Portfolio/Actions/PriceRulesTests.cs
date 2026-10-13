using XDrishti.Application.Portfolio.Actions;
using XDrishti.Application.Portfolio.Actions.Rules;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Actions.ActionFixtures;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>Below cost and large gain (acceptance criteria 1 and 7 of P2-09): at and around the thresholds, unpriced and closed holdings, custom thresholds. A position of 10 shares bought at 100.</summary>
public sealed class PriceRulesTests
{
    [Theory]
    [InlineData(100, null)]
    [InlineData(90.01, null)]
    [InlineData(90, ActionSeverities.Medium)]
    [InlineData(80.01, ActionSeverities.Medium)]
    [InlineData(80, ActionSeverities.High)]
    [InlineData(50, ActionSeverities.High)]
    public void Below_cost_fires_at_minus_10_percent_as_medium_and_at_minus_20_as_high(double price, string? severity)
    {
        var hits = BelowCostRule.Evaluate(Context([Pos("ABC", (decimal)price)]));

        hits.Select(h => h.Item.Severity).SingleOrDefault().ShouldBe(severity);
    }

    [Fact]
    public void Below_cost_carries_the_numbers_and_says_them_in_words()
    {
        var hit = BelowCostRule.Evaluate(Context([Pos("ABC", 76.6m)])).ShouldHaveSingleItem();

        hit.Item.Rule.ShouldBe("below_cost");
        hit.Item.Id.ShouldBe($"below_cost:{hit.Item.HoldingId}");
        hit.Item.Title.ShouldBe("Below cost");
        hit.Item.Detail.ShouldBe("Down 23.4 % from cost (₹1,000 invested)");
        hit.Item.Numbers.PnlPct.ShouldBe(-23.4m);
        (hit.Item.Numbers.Pnl, hit.Item.Numbers.Invested, hit.Item.Numbers.Value, hit.Item.Numbers.ThresholdPct).ShouldBe((-234m, 1000m, 766m, 20m));
        hit.Item.Suggestions.ShouldBe(["open", "set_alert", "add_to_basket", "snooze", "done"]);
        hit.Item.State.ShouldBe("open");
        hit.Item.Symbol.ShouldBe("ABC");
        hit.Item.InstrumentId.ShouldBe(InstrumentOf("ABC"));
    }

    [Fact]
    public void A_holding_that_is_not_in_the_master_gets_no_alert_or_basket_suggestion()
    {
        var hit = BelowCostRule.Evaluate(Context([Pos("ABC", 70m, mapped: false)])).ShouldHaveSingleItem();

        hit.Item.InstrumentId.ShouldBeNull();
        hit.Item.Suggestions.ShouldBe(["open", "snooze", "done"]);
    }

    [Fact]
    public void An_unpriced_holding_never_fires_a_price_rule()
    {
        var context = Context([Pos("ABC", 0m)]);

        BelowCostRule.Evaluate(context).ShouldBeEmpty();
        LargeGainRule.Evaluate(context).ShouldBeEmpty();
    }

    [Fact]
    public void A_closed_holding_is_not_evaluated()
    {
        var closed = Pos("ABC", 50m) with { Status = "closed" };

        BelowCostRule.Evaluate(Context([closed])).ShouldBeEmpty();
    }

    [Fact]
    public void The_thresholds_come_from_the_context()
    {
        var thresholds = ActionThresholds.Defaults with { BelowCostPct = 5m, BelowCostHighPct = 8m };

        var hits = BelowCostRule.Evaluate(Context([Pos("MED", 94m), Pos("HIGH", 91.9m), Pos("NONE", 95.1m)], thresholds));

        BySymbol(hits).ToDictionary(h => h.Key, h => h.Value.Item.Severity).ShouldBe(new Dictionary<string, string> { ["MED"] = "medium", ["HIGH"] = "high" });
    }

    [Theory]
    [InlineData(124.99, false)]
    [InlineData(125, true)]
    [InlineData(300, true)]
    [InlineData(100, false)]
    public void Large_gain_fires_at_plus_25_percent_as_info(double price, bool fires)
    {
        var hits = LargeGainRule.Evaluate(Context([Pos("ABC", (decimal)price)]));

        hits.Count.ShouldBe(fires ? 1 : 0);
        hits.ShouldAllBe(h => h.Item.Severity == "info");
    }

    [Fact]
    public void Large_gain_carries_the_numbers_and_the_threshold()
    {
        var hit = LargeGainRule.Evaluate(Context([Pos("ABC", 131.2m)])).ShouldHaveSingleItem();

        hit.Item.Detail.ShouldBe("Up 31.2 % on cost (₹1,000 invested)");
        (hit.Item.Numbers.PnlPct, hit.Item.Numbers.ThresholdPct).ShouldBe((31.2m, 25m));
        hit.Size.ShouldBe(31.2m);
    }

    [Fact]
    public void The_gain_threshold_is_editable()
    {
        var hits = LargeGainRule.Evaluate(Context([Pos("ABC", 110m)], ActionThresholds.Defaults with { GainPct = 10m }));

        hits.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(1_000, "₹1,000")]
    [InlineData(120_000, "₹1.20 L")]
    [InlineData(99_999, "₹99,999")]
    [InlineData(100_000, "₹1.00 L")]
    [InlineData(23_500_000, "₹2.35 Cr")]
    public void Rupees_are_written_in_lakhs_and_crores(int amount, string text)
    {
        ActionText.Rupees(amount).ShouldBe(text);
    }
}
