using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using static XDrishti.Application.Tests.Portfolio.HoldingFixtures;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>Day change against the previous close (ADR 0019 §4): per holding, per group and in total, with the holdings that cannot be measured left out and counted.</summary>
public sealed class DayChangeCalculatorTests
{
    private static readonly DateOnly Bought = new(2025, 1, 1);

    private static HoldingData Held(decimal last, decimal? previous, long qty = 100, string symbol = "EXMPL", string status = "open") =>
        Holding(last, symbol, status, null, Open(qty, 50m, Bought)).Quoted(previous);

    [Theory]
    [InlineData(110.0, 100.0, 1000.0, 10.0)] // gain
    [InlineData(95.0, 100.0, -500.0, -5.0)] // loss
    [InlineData(100.0, 100.0, 0.0, 0.0)] // zero
    [InlineData(101.5, 100.0, 150.0, 1.5)]
    public void A_holding_changes_by_qty_times_the_difference_and_by_the_difference_over_the_previous_close(double last, double previous, double amount, double pct)
    {
        var change = DayChangeCalculator.ForHolding(Held((decimal)last, (decimal)previous, qty: 100)).ShouldNotBeNull();

        (change.Amount, change.Pct).ShouldBe(((decimal)amount, (decimal)pct));
    }

    [Fact]
    public void The_percentage_is_rounded_to_four_decimals()
    {
        var change = DayChangeCalculator.ForHolding(Held(101m, 300m, qty: 3)).ShouldNotBeNull();

        (change.Amount, change.Pct).ShouldBe((-597m, -66.3333m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Without_a_usable_previous_close_the_day_change_is_unavailable(double previous)
    {
        DayChangeCalculator.ForHolding(Held(110m, (decimal)previous)).ShouldBeNull();
    }

    [Fact]
    public void Without_any_previous_close_the_day_change_is_unavailable()
    {
        DayChangeCalculator.ForHolding(Held(110m, null)).ShouldBeNull();
    }

    [Fact]
    public void A_closed_holding_and_one_without_a_price_have_no_day_change()
    {
        DayChangeCalculator.ForHolding(Held(110m, 100m, status: "closed")).ShouldBeNull();
        DayChangeCalculator.ForHolding(Held(0m, 100m)).ShouldBeNull();
    }

    [Fact]
    public void A_group_sums_the_amounts_and_takes_the_percentage_on_the_sum_of_the_previous_values_not_as_an_average()
    {
        var small = Held(110m, 100m, qty: 10, symbol: "AAA"); // +100 on 1,000 (+10 %)
        var big = Held(990m, 1000m, qty: 100, symbol: "BBB"); // -1,000 on 100,000 (-1 %)

        var group = DayChangeCalculator.ForGroup([small, big]);

        (group.Amount, group.Pct, group.Included, group.Excluded).ShouldBe((-900m, -0.8911m, 2, 0));
    }

    [Fact]
    public void The_total_equals_the_sum_of_the_holdings_day_changes()
    {
        var holdings = new[] { Held(110m, 100m, 10, "AAA"), Held(990m, 1000m, 100, "BBB"), Held(55.55m, 54.45m, 7, "CCC") };

        var group = DayChangeCalculator.ForGroup(holdings);

        group.Amount.ShouldBe(holdings.Sum(h => DayChangeCalculator.ForHolding(h)!.Amount));
    }

    [Fact]
    public void A_holding_without_a_previous_close_is_left_out_and_counted_never_taken_as_zero()
    {
        var withClose = Held(110m, 100m, 10, "AAA");
        var without = Held(500m, null, 100, "BBB");

        var group = DayChangeCalculator.ForGroup([withClose, without]);

        (group.Amount, group.Pct, group.Included, group.Excluded).ShouldBe((100m, 10m, 1, 1));
    }

    [Fact]
    public void When_every_holding_is_left_out_the_totals_are_null_not_zero()
    {
        var group = DayChangeCalculator.ForGroup([Held(110m, null), Held(0m, 100m)]);

        (group.Amount, group.Pct, group.Included, group.Excluded).ShouldBe((null, null, 0, 2));
    }

    [Fact]
    public void Closed_holdings_are_ignored_and_not_counted_as_excluded()
    {
        var group = DayChangeCalculator.ForGroup([Held(110m, 100m, 10, "AAA"), Held(70m, 60m, 0, "BBB", status: "closed")]);

        (group.Amount, group.Included, group.Excluded).ShouldBe((100m, 1, 0));
    }

    [Fact]
    public void No_holdings_at_all_is_an_empty_group()
    {
        var group = DayChangeCalculator.ForGroup([]);

        (group.Amount, group.Pct, group.Included, group.Excluded).ShouldBe((null, null, 0, 0));
    }

    [Fact]
    public void A_quote_moves_value_pnl_and_xirr_consistently_because_they_all_read_the_last_price()
    {
        var before = Holding(60m, lots: Open(100, 50m, new DateOnly(2024, 10, 11)));
        var after = before with { LastPrice = 66m, PreviousClose = 59m, PriceSource = HoldingData.QuoteSource };

        var a = ReturnsCalculator.ForHolding(before, Valuation);
        var b = ReturnsCalculator.ForHolding(after, Valuation);

        (b.Value - a.Value, b.Pnl - a.Pnl).ShouldBe((600m, 600m)); // 100 shares x 6
        b.Xirr.Rate.ShouldNotBeNull().ShouldBeGreaterThan(a.Xirr.Rate!.Value);
        b.Xirr.Rate!.Value.ShouldBe(0.14891, 1e-4); // 6,600 / 5,000 over 730 days: sqrt(1.32) - 1
        DayChangeCalculator.ForHolding(after)!.Amount.ShouldBe(700m); // 100 x (66 - 59)
    }
}
