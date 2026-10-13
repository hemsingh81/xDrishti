using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Actions.Rules;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Application.Tests.Portfolio.History;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Actions.ActionFixtures;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>
/// Lagging Nifty (acceptance criterion 5): a holding held for more than 180 days whose XIRR is more than 10 points below the XIRR of the same cash flows in the index. The holding is bought on 2025-01-02 at 100 and now worth 100 (XIRR 0 %);
/// the index goes from 1,000 to 1,300 in 648 days (about 15.9 % a year) unless a test says otherwise. Today is 2026-10-12.
/// </summary>
public sealed class LaggingNiftyRuleTests
{
    private static readonly DateOnly Bought = new(2025, 1, 2);

    private static readonly DailyClose[] Rising = Index((Bought, 1000m), (Today, 1300m));

    private static HoldingData Flat(string symbol = "LAG", decimal price = 100m, DateOnly? acquired = null) => Pos(symbol, price, 10, 100m, acquired ?? Bought);

    [Fact]
    public void A_holding_far_behind_the_index_fires_with_both_rates_and_the_gap()
    {
        var result = LaggingNiftyRule.Evaluate(Context([Flat()]), Rising, HistoryData.Weekdays);

        var hit = result.Hits.ShouldHaveSingleItem();
        (hit.Item.Rule, hit.Item.Severity, hit.Item.Symbol).ShouldBe(("lagging_nifty", "medium", "LAG"));
        var n = hit.Item.Numbers;
        n.HeldDays.ShouldBe(648);
        n.XirrPct.ShouldBe(0m);
        n.BenchmarkXirrPct!.Value.ShouldBeInRange(15.5m, 16.5m);
        n.GapPp.ShouldBe(-n.BenchmarkXirrPct);
        n.ThresholdPp.ShouldBe(10m);
        hit.Item.Detail.ShouldStartWith("XIRR 0.0 % against 15.");
        result.Unavailable.ShouldBeEmpty();
    }

    [Fact]
    public void A_gap_inside_the_limit_does_not_fire()
    {
        var index = Index((Bought, 1000m), (Today, 1100m)); // about 5.6 % a year

        LaggingNiftyRule.Evaluate(Context([Flat()]), index, HistoryData.Weekdays).Hits.ShouldBeEmpty();
    }

    [Fact]
    public void The_gap_limit_is_a_threshold()
    {
        var thresholds = ActionThresholds.Defaults with { LagPp = 20m };

        LaggingNiftyRule.Evaluate(Context([Flat()], thresholds), Rising, HistoryData.Weekdays).Hits.ShouldBeEmpty();
    }

    [Fact]
    public void A_holding_that_beats_the_index_does_not_fire()
    {
        LaggingNiftyRule.Evaluate(Context([Flat(price: 200m)]), Rising, HistoryData.Weekdays).Hits.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2026-04-14", true)] // 181 days
    [InlineData("2026-04-15", false)] // 180 days: not more than 180
    [InlineData("2026-05-01", false)]
    public void The_holding_must_have_been_held_for_more_than_the_limit_by_its_oldest_open_lot(string acquired, bool eligible)
    {
        var holding = Flat(acquired: DateOnly.Parse(acquired, System.Globalization.CultureInfo.InvariantCulture));
        var index = Index((new DateOnly(2026, 1, 1), 1000m), (Today, 1300m));

        LaggingNiftyRule.Eligible(Context([holding])).Count.ShouldBe(eligible ? 1 : 0);
        LaggingNiftyRule.Evaluate(Context([holding]), index, HistoryData.Weekdays).Unavailable.Count.ShouldBe(0); // never counted as unavailable when it is not eligible
    }

    [Fact]
    public void The_oldest_open_lot_decides_the_age_not_a_closed_one()
    {
        var holding = Flat().WithLots(
            HoldingFixtures.Closed(5, 100m, Bought, new DateOnly(2025, 6, 2), 110m),
            HoldingFixtures.Open(10, 100m, new DateOnly(2026, 8, 1)));

        LaggingNiftyRule.Eligible(Context([holding])).ShouldBeEmpty();
    }

    [Fact]
    public void Index_bars_that_start_after_the_first_flow_make_the_holding_unavailable_never_lagging_or_fine()
    {
        var index = Index((new DateOnly(2025, 6, 2), 1100m), (Today, 1300m));

        var result = LaggingNiftyRule.Evaluate(Context([Flat()]), index, HistoryData.Weekdays);

        result.Hits.ShouldBeEmpty();
        result.Unavailable.Select(h => h.TradingSymbol).ShouldBe(["LAG"]);
    }

    [Fact]
    public void Without_index_bars_or_a_calendar_every_eligible_holding_is_unavailable()
    {
        LaggingNiftyRule.Evaluate(Context([Flat("A"), Flat("B")]), null, null).Unavailable.Count.ShouldBe(2);
        LaggingNiftyRule.Evaluate(Context([Flat("A")]), [], HistoryData.Weekdays).Unavailable.Count.ShouldBe(1);
    }

    [Fact]
    public void A_rate_that_is_not_meaningful_makes_the_holding_unavailable()
    {
        var wild = Pos("WILD", 5000m, 10, 1m, Bought); // 5,000 times its cost in 648 days: beyond 1,000 % a year

        var result = LaggingNiftyRule.Evaluate(Context([wild]), Rising, HistoryData.Weekdays);

        (result.Hits.Count, result.Unavailable.Count).ShouldBe((0, 1));
    }

    [Fact]
    public void An_unpriced_holding_is_neither_judged_nor_counted()
    {
        var result = LaggingNiftyRule.Evaluate(Context([Flat(price: 0m)]), Rising, HistoryData.Weekdays);

        (result.Hits.Count, result.Unavailable.Count).ShouldBe((0, 0));
    }

    [Fact]
    public void The_comparison_uses_the_cash_flows_of_the_holding_alone()
    {
        // LAG lags; GOOD, bought on the same day, doubled.
        var result = LaggingNiftyRule.Evaluate(Context([Flat("LAG"), Flat("GOOD", price: 200m)]), Rising, HistoryData.Weekdays);

        result.Hits.Select(h => h.Item.Symbol).ShouldBe(["LAG"]);
    }

    [Fact]
    public void A_purchase_dated_on_a_saturday_takes_part_on_the_next_trading_day()
    {
        // 2025-01-04 is a Saturday: the flow is dated then, the index units are bought at Monday's close, so an index whose bars start on Monday is enough.
        var saturday = Flat(acquired: new DateOnly(2025, 1, 4));

        var fromMonday = LaggingNiftyRule.Evaluate(Context([saturday]), Index((new DateOnly(2025, 1, 6), 1000m), (Today, 1300m)), HistoryData.Weekdays);
        var fromTuesday = LaggingNiftyRule.Evaluate(Context([saturday]), Index((new DateOnly(2025, 1, 7), 1000m), (Today, 1300m)), HistoryData.Weekdays);

        fromMonday.Hits.ShouldHaveSingleItem().Item.Numbers.HeldDays.ShouldBe(646);
        (fromTuesday.Hits.Count, fromTuesday.Unavailable.Count).ShouldBe((0, 1)); // no close on or before the first effective day
    }
}
