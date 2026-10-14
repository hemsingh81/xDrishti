using XDrishti.Application.Portfolio.Returns;
using static XDrishti.Application.Tests.Portfolio.HoldingFixtures;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>Profit/loss and XIRR from lots, valued on 2026-10-11. XIRR references: Python bisection (see <see cref="XirrTests"/>).</summary>
public sealed class ReturnsCalculatorTests
{
    private static readonly DateOnly TwoYearsAgo = new(2024, 10, 11);

    [Fact]
    public void A_single_lot_gives_invested_value_profit_percent_and_xirr()
    {
        var holding = Holding(60m, lots: Open(100, 50m, TwoYearsAgo));

        var r = ReturnsCalculator.ForHolding(holding, Valuation);

        (r.Invested, r.Value, r.Pnl, r.PnlPct, r.RealisedPnl, r.RealisedEstimated).ShouldBe((5000m, 6000m, 1000m, 20m, 0m, false));
        r.Xirr.State.ShouldBe(XirrState.Ok);
        r.Xirr.Rate.ShouldNotBeNull().ShouldBe(0.095445115010, 1e-6); // sqrt(1.2) - 1 over 730 days
    }

    [Fact]
    public void Several_lots_add_up_and_the_xirr_uses_every_dated_outflow()
    {
        var holding = Holding(180m, lots: [Open(100, 50m, TwoYearsAgo), Open(10, 200m, new DateOnly(2026, 4, 11))]);

        var r = ReturnsCalculator.ForHolding(holding, Valuation);

        // invested 5,000 + 2,000; value 110 * 180 = 19,800 → flows -5,000 / -2,000 / +19,800
        (r.Invested, r.Value, r.Pnl).ShouldBe((7000m, 19800m, 12800m));
        r.PnlPct.ShouldBe(182.8571m);
        r.Xirr.State.ShouldBe(XirrState.Ok);
    }

    [Fact]
    public void A_group_xirr_comes_from_the_union_of_the_cash_flows_not_from_an_average()
    {
        var a = Holding(60m, "AAA", lots: Open(100, 50m, TwoYearsAgo));
        var b = Holding(180m, "BBB", lots: Open(10, 200m, new DateOnly(2026, 4, 11)));
        b = b with { Qty = 10 };
        // b is a loss: 10 * 180 = 1,800 against 2,000 (price 180 → value 1,800)

        var group = ReturnsCalculator.ForGroup([a, b], Valuation);
        var xa = ReturnsCalculator.ForHolding(a, Valuation).Xirr;
        var xb = ReturnsCalculator.ForHolding(b, Valuation).Xirr;

        (group.Invested, group.Value, group.Pnl).ShouldBe((7000m, 7800m, 800m));
        group.Xirr.Rate.ShouldNotBeNull().ShouldBe(0.070556210400, 1e-6); // flows -5,000 / -2,000 / +7,800
        Math.Abs(group.Xirr.Rate!.Value - ((xa.Rate!.Value + xb.Rate!.Value) / 2)).ShouldBeGreaterThan(0.05);
    }

    [Fact]
    public void Closed_lots_add_realised_profit_marked_estimated_and_their_sale_to_the_xirr()
    {
        // 100 shares bought on 2025-01-01 at 50; 30 were sold on 2026-06-01 at an estimated 90; the rest is worth 100 each.
        var holding = Holding(100m, lots:
        [
            Open(70, 50m, new DateOnly(2025, 1, 1)),
            Closed(30, 50m, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 1), 90m),
        ]);

        var r = ReturnsCalculator.ForHolding(holding, Valuation);

        (r.Invested, r.Value, r.Pnl, r.RealisedPnl, r.RealisedEstimated).ShouldBe((3500m, 7000m, 3500m, 1200m, true));
        r.Xirr.Rate.ShouldNotBeNull().ShouldBe(0.487314759381, 1e-6); // flows -3,500 -1,500 +2,700 +7,000
    }

    [Fact]
    public void A_sale_at_the_real_price_is_not_marked_estimated()
    {
        var holding = Holding(100m, lots: [Open(70, 50m, new DateOnly(2025, 1, 1)), Closed(30, 50m, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 1), 90m, estimated: false)]);

        ReturnsCalculator.ForHolding(holding, Valuation).RealisedEstimated.ShouldBeFalse();
    }

    [Fact]
    public void A_closed_holding_has_only_realised_results_and_a_xirr_of_its_history()
    {
        var holding = Holding(100m, status: "closed", lots: Closed(100, 50m, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 1), 90m));

        var r = ReturnsCalculator.ForHolding(holding, Valuation);

        (r.Invested, r.Value, r.Pnl, r.PnlPct, r.RealisedPnl).ShouldBe((0m, 0m, 0m, (decimal?)null, 4000m));
        r.Xirr.Rate.ShouldNotBeNull().ShouldBe(0.515551390569, 1e-6);
    }

    [Fact]
    public void A_lot_bought_today_has_profit_but_no_xirr()
    {
        var holding = Holding(105m, lots: Open(10, 100m, Valuation));

        var r = ReturnsCalculator.ForHolding(holding, Valuation);

        (r.Pnl, r.PnlPct).ShouldBe((50m, 5m));
        r.Xirr.State.ShouldBe(XirrState.Insufficient);
    }

    [Fact]
    public void A_lot_younger_than_thirty_days_has_no_xirr()
    {
        var r = ReturnsCalculator.ForHolding(Holding(105m, lots: Open(10, 100m, Valuation.AddDays(-29))), Valuation);

        r.Xirr.State.ShouldBe(XirrState.Insufficient);
    }

    [Fact]
    public void A_negative_return_shows_a_loss_and_a_negative_xirr()
    {
        var holding = Holding(42m, lots: Open(100, 50m, new DateOnly(2024, 5, 20)));

        var r = ReturnsCalculator.ForHolding(holding, Valuation);

        (r.Pnl, r.PnlPct).ShouldBe((-800m, -16m));
        r.Xirr.Rate.ShouldNotBeNull().ShouldBe(-0.070225769749, 1e-6);
    }

    [Fact]
    public void A_holding_without_a_price_has_its_cost_but_no_value_profit_or_xirr_it_is_not_a_total_loss()
    {
        var holding = Holding(0m, lots: Open(100, 50m, TwoYearsAgo));

        var r = ReturnsCalculator.ForHolding(holding, Valuation);

        holding.IsPriced.ShouldBeFalse();
        (r.Invested, r.Value, r.Pnl, r.PnlPct, r.Xirr).ShouldBe((5000m, (decimal?)null, (decimal?)null, (decimal?)null, new XirrResult(XirrState.NoPrice, null)));
    }

    [Fact]
    public void A_closed_holding_counts_as_priced_whatever_its_last_price()
    {
        var holding = Holding(0m, status: "closed", lots: Closed(100, 50m, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 1), 90m));

        holding.IsPriced.ShouldBeTrue();
        ReturnsCalculator.ForHolding(holding, Valuation).RealisedPnl.ShouldBe(4000m);
    }

    [Fact]
    public void Unpriced_holdings_are_left_out_of_a_group_but_their_realised_results_stay()
    {
        var priced = Holding(60m, "AAA", lots: Open(100, 50m, TwoYearsAgo));
        var unpriced = Holding(0m, "NEW", lots: [Open(10, 300m, TwoYearsAgo), Closed(5, 100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 1), 120m)]);

        var group = ReturnsCalculator.ForGroup([priced, unpriced], Valuation);
        var alone = ReturnsCalculator.ForGroup([priced], Valuation);

        (group.Invested, group.Value, group.Pnl, group.PnlPct).ShouldBe((5000m, (decimal?)6000m, (decimal?)1000m, (decimal?)20m));
        group.Xirr.ShouldBe(alone.Xirr);
        (group.RealisedPnl, group.RealisedEstimated).ShouldBe((100m, true));
    }

    [Fact]
    public void A_group_of_only_unpriced_holdings_has_nothing_to_value()
    {
        var group = ReturnsCalculator.ForGroup([Holding(0m, "NEW", lots: Open(10, 300m, TwoYearsAgo))], Valuation);

        (group.Invested, group.Value, group.Xirr.State).ShouldBe((0m, (decimal?)0m, XirrState.NoData));
    }

    [Fact]
    public void An_open_lot_of_an_unpriced_holding_has_no_figures_but_a_closed_lot_keeps_its_result()
    {
        var open = Open(10, 300m, TwoYearsAgo);
        var closed = Closed(5, 100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 1), 120m);
        var holding = Holding(0m, "NEW", lots: [open, closed]);

        var openFigures = ReturnsCalculator.ForLot(holding, open, Valuation);
        var closedFigures = ReturnsCalculator.ForLot(holding, closed, Valuation);

        (openFigures.Invested, openFigures.Value, openFigures.Xirr.State).ShouldBe((3000m, (decimal?)null, XirrState.NoPrice));
        (closedFigures.Pnl, closedFigures.Xirr.State).ShouldBe(((decimal?)100m, XirrState.Ok));
    }

    [Fact]
    public void A_holding_without_lots_has_no_data()
    {
        var r = ReturnsCalculator.ForHolding(Holding(60m), Valuation);

        (r.Invested, r.PnlPct).ShouldBe((0m, (decimal?)null));
        r.Xirr.State.ShouldBe(XirrState.NoData);
    }

    [Fact]
    public void An_extreme_price_is_not_meaningful_instead_of_a_wrong_number()
    {
        var r = ReturnsCalculator.ForHolding(Holding(1_000_000m, lots: Open(100, 50m, Valuation.AddDays(-60))), Valuation);

        r.Xirr.State.ShouldBe(XirrState.NotMeaningful);
    }

    [Fact]
    public void A_lot_has_its_own_figures_open_at_the_last_price_and_closed_at_its_sale()
    {
        var open = Open(70, 50m, new DateOnly(2025, 1, 1));
        var closed = Closed(30, 50m, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 1), 90m);
        var holding = Holding(100m, lots: [open, closed]);

        var openFigures = ReturnsCalculator.ForLot(holding, open, Valuation);
        var closedFigures = ReturnsCalculator.ForLot(holding, closed, Valuation);

        (openFigures.Invested, openFigures.Value, openFigures.Pnl, openFigures.PnlPct).ShouldBe((3500m, 7000m, 3500m, 100m));
        openFigures.Xirr.Rate.ShouldNotBeNull().ShouldBe(0.477616216674, 1e-6);
        (closedFigures.Invested, closedFigures.Value, closedFigures.Pnl, closedFigures.RealisedPnl, closedFigures.RealisedEstimated).ShouldBe((1500m, 2700m, 1200m, 1200m, true));
        closedFigures.Xirr.Rate.ShouldNotBeNull().ShouldBe(0.515551390569, 1e-6);
    }

    [Fact]
    public void The_sum_of_the_holdings_equals_the_group()
    {
        var a = Holding(60m, "AAA", lots: Open(100, 50m, TwoYearsAgo));
        var b = Holding(180m, "BBB", lots: [Open(10, 200m, new DateOnly(2026, 4, 11)), Closed(5, 150m, new DateOnly(2025, 3, 3), new DateOnly(2026, 2, 2), 170m)]);

        var group = ReturnsCalculator.ForGroup([a, b], Valuation);
        var parts = new[] { a, b }.Select(h => ReturnsCalculator.ForHolding(h, Valuation)).ToList();

        group.Invested.ShouldBe(parts.Sum(p => p.Invested));
        group.Value.ShouldBe(parts.Sum(p => p.Value));
        group.Pnl.ShouldBe(parts.Sum(p => p.Pnl));
        group.RealisedPnl.ShouldBe(parts.Sum(p => p.RealisedPnl));
    }

    [Fact]
    public void Without_the_xirr_every_other_figure_of_a_holding_is_the_same_and_the_rate_is_left_unworked()
    {
        var holding = Holding(180m, lots: [Open(100, 50m, TwoYearsAgo), Open(10, 200m, new DateOnly(2026, 4, 11)), Closed(5, 40m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 1), 70m)]);

        var full = ReturnsCalculator.ForHolding(holding, Valuation);
        var money = ReturnsCalculator.ForHolding(holding, Valuation, withXirr: false);

        money.ShouldBe(full with { Xirr = XirrResult.NoData });
        full.Xirr.State.ShouldBe(XirrState.Ok);
    }
}
