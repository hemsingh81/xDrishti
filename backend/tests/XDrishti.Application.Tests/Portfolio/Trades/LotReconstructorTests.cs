using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Trades.ReconFixtures;

namespace XDrishti.Application.Tests.Portfolio.Trades;

/// <summary>Rebuilding a holding's lots from its trades (ADR 0023; P2-03 criteria 6 to 10). Pure: the same trades always give the same lots.</summary>
public sealed class LotReconstructorTests
{
    [Fact]
    public void Without_trades_there_is_one_lot_of_the_brokers_quantity_and_cost_dated_by_the_first_import_and_flagged_default()
    {
        var result = Run(40, 123.4567m, []);

        result.Applicable.ShouldBeTrue();
        result.Lots.ShouldBe([new LotPlan(FirstImport, 40, 123.4567m, LotSource.Import, UsesDefaultDate: true)]);
        (result.Net, result.OpeningQty, result.RealisedPnl, result.AdjustedFor).ShouldBe((0L, 40L, 0m, 0));
    }

    [Fact]
    public void Trades_that_explain_the_whole_holding_leave_no_opening_lot()
    {
        var result = Run(15, 99m, [Buy("2024-03-05", 10, 90m), Buy("2024-06-12", 5, 120m)]);

        result.Lots.ShouldBe(
        [
            new LotPlan(D("2024-03-05"), 10, 90m, LotSource.Trade, UsesDefaultDate: false),
            new LotPlan(D("2024-06-12"), 5, 120m, LotSource.Trade, UsesDefaultDate: false),
        ]);
        (result.Net, result.OpeningQty).ShouldBe((15L, 0L));
    }

    [Fact]
    public void What_the_trades_do_not_explain_is_an_opening_lot_the_day_before_the_first_trade_when_that_is_before_the_first_import_date_and_costs_what_is_left()
    {
        var result = Run(100, 50m, [Buy("2024-03-05", 20, 60m)]);

        var opening = result.Lots.Single(l => l.Source == LotSource.Import);
        (opening.AcquiredOn, opening.Qty, opening.UnitCost, opening.UsesDefaultDate).ShouldBe((D("2024-03-04"), 80L, 47.5m, true));
        result.OpeningQty.ShouldBe(80);
    }

    [Fact]
    public void The_opening_lot_keeps_the_first_import_date_when_that_is_before_the_first_trade()
    {
        var result = Run(100, 50m, [Buy("2024-03-05", 20, 60m)], firstImport: D("2023-01-01"));

        result.Lots.Single(l => l.Source == LotSource.Import).AcquiredOn.ShouldBe(D("2023-01-01"));
    }

    [Fact]
    public void The_open_lots_cost_what_the_broker_says_in_total()
    {
        var result = Run(100, 50.1234m, [Buy("2024-03-05", 20, 60m), Buy("2024-04-01", 5, 70.5m)]);

        result.OpenQty().ShouldBe(100);
        result.OpenCost().ShouldBe(100 * 50.1234m, 0.01m);
    }

    [Fact]
    public void A_holding_fully_sold_down_by_the_trades_keeps_only_what_is_open()
    {
        var result = Run(0, 0m, [Buy("2024-03-05", 10, 90m), Sell("2024-04-05", 10, 100m)]);

        result.OpenQty().ShouldBe(0);
        result.Lots.ShouldBe([new LotPlan(D("2024-03-05"), 10, 90m, LotSource.Trade, false, D("2024-04-05"), 100m)]);
        result.RealisedPnl.ShouldBe(100m);
    }

    [Fact]
    public void A_sale_closes_the_opening_lot_first_because_it_is_the_oldest_and_splits_it()
    {
        var result = Run(30, 100m, [Buy("2024-02-01", 10, 110m), Sell("2024-03-01", 15, 120m)]);

        result.Lots.Where(l => l.IsOpen).Select(l => (l.Source, l.Qty)).ShouldBe([(LotSource.Import, 20L), (LotSource.Trade, 10L)]);
        var piece = result.Lots.Single(l => !l.IsOpen);
        (piece.Source, piece.Qty, piece.AcquiredOn, piece.ClosedOn, piece.ClosePrice).ShouldBe((LotSource.Import, 15L, D("2024-01-31"), D("2024-03-01"), 120m));
        piece.UsesDefaultDate.ShouldBeTrue();
        piece.UnitCost.ShouldBe(result.Lots.First(l => l.IsOpen && l.Source == LotSource.Import).UnitCost);
    }

    [Fact]
    public void A_sale_larger_than_the_opening_lot_goes_on_into_the_next_lots_first_in_first_out()
    {
        var result = Run(7, 100m, [Buy("2024-02-01", 10, 110m), Buy("2024-02-10", 10, 112m), Sell("2024-03-01", 8, 120m), Sell("2024-03-02", 6, 125m)]);

        result.OpeningQty.ShouldBe(1);
        result.Lots.Where(l => !l.IsOpen).Select(l => (l.Source, l.Qty, l.ClosedOn, l.ClosePrice)).ShouldBe(
        [
            (LotSource.Import, 1L, (DateOnly?)D("2024-03-01"), (decimal?)120m),
            (LotSource.Trade, 7L, D("2024-03-01"), 120m),
            (LotSource.Trade, 3L, D("2024-03-02"), 125m),
            (LotSource.Trade, 3L, D("2024-03-02"), 125m),
        ]);
        result.Lots.Where(l => l.IsOpen).Select(l => (l.Qty, l.UnitCost)).ShouldBe([(7L, 112m)]);
    }

    [Fact]
    public void Closed_pieces_carry_the_real_sale_day_and_price_and_the_realised_profit_is_their_sum()
    {
        var result = Run(5, 0m, [Buy("2024-02-01", 10, 100m), Buy("2024-02-02", 10, 110m), Sell("2024-03-01", 15, 130m)]);

        result.RealisedPnl.ShouldBe((10 * (130m - 100m)) + (5 * (130m - 110m)));
        result.Lots.Where(l => !l.IsOpen).ShouldAllBe(l => l.ClosedOn == D("2024-03-01") && l.ClosePrice == 130m);
    }

    [Fact]
    public void On_one_day_buys_come_before_sells_whatever_the_order_they_were_stored_in()
    {
        var result = Run(0, 0m, [Sell("2024-03-05", 5, 105m, seq: 1), Buy("2024-03-05", 5, 100m, seq: 2)]);

        result.Applicable.ShouldBeTrue();
        result.Lots.ShouldBe([new LotPlan(D("2024-03-05"), 5, 100m, LotSource.Trade, false, D("2024-03-05"), 105m)]);
    }

    [Fact]
    public void On_one_day_the_same_side_is_ordered_by_time_then_by_the_order_stored()
    {
        var result = Run(
            3,
            0m,
            [
                Buy("2024-03-05", 1, 103m, "10:00", seq: 1),
                Buy("2024-03-05", 1, 101m, "09:00", seq: 2),
                Buy("2024-03-05", 1, 102m, "09:00", seq: 3),
            ]);

        result.Lots.Select(l => l.UnitCost).ShouldBe([101m, 102m, 103m]);
    }

    [Fact]
    public void More_shares_explained_than_the_broker_holds_is_a_mismatch_with_the_numbers()
    {
        var result = Run(10, 100m, [Buy("2024-03-05", 15, 90m)]);

        result.Applicable.ShouldBeFalse();
        (result.Reason, result.Net, result.OpeningQty, result.Qty).ShouldBe((MismatchReasons.NegativeOpening, 15L, -5L, 10L));
        result.Lots.ShouldBeEmpty();
    }

    [Fact]
    public void A_sale_before_there_is_anything_to_sell_is_a_mismatch()
    {
        var result = Run(5, 100m, [Sell("2024-03-05", 10, 90m), Buy("2024-04-01", 15, 80m)]);

        (result.Applicable, result.Reason).ShouldBe((false, MismatchReasons.SellExceedsOpen));
    }

    [Fact]
    public void Sales_alone_sell_from_the_opening_lot_which_the_broker_quantity_leaves_open_on_top()
    {
        var result = Run(5, 40m, [Sell("2024-03-05", 2, 90m)]);

        result.OpeningQty.ShouldBe(7);
        result.Lots.Select(l => (l.Qty, l.UnitCost, l.ClosedOn)).ShouldBe([(5L, 40m, (DateOnly?)null), (2L, 40m, D("2024-03-05"))]);
        result.RealisedPnl.ShouldBe(2 * (90m - 40m));
    }

    [Fact]
    public void An_opening_lot_that_the_sales_use_up_costs_what_the_average_cost_implies_and_the_pieces_sold_from_it_cost_that()
    {
        var result = Run(10, 40m, [Sell("2024-03-05", 3, 90m), Buy("2024-03-06", 10, 50m), Sell("2024-03-07", 3, 95m)]);

        // 10 held; bought 10, sold 6: the opening 6 are all sold. c0 = (40 x (6 + 10) - 500) / 6 = 23.3333.
        result.OpeningQty.ShouldBe(6);
        result.Lots.Where(l => l.Source == LotSource.Import).ShouldAllBe(l => !l.IsOpen && l.UnitCost == 23.3333m);
        result.RealisedPnl.ShouldBe((3 * (90m - 23.3333m)) + (3 * (95m - 23.3333m)));
    }

    [Fact]
    public void The_average_cost_is_by_the_weighted_average_method_so_a_sale_does_not_change_the_cost_of_the_opening_shares()
    {
        // Q = 100 at A = 65; 150 bought at 80, 100 sold at 90: P0 = 50, c0 = (65 x 200 - 12,000) / 50 = 20.
        var result = Run(100, 65m, [Buy("2024-03-05", 150, 80m), Sell("2024-04-01", 100, 90m)]);

        result.OpeningQty.ShouldBe(50);
        var pieces = result.Lots.Where(l => !l.IsOpen).ToList();
        pieces.Select(p => (p.Source, p.Qty, p.UnitCost)).ShouldBe([(LotSource.Import, 50L, 20m), (LotSource.Trade, 50L, 80m)]);
        (pieces[0].Qty * (pieces[0].ClosePrice!.Value - pieces[0].UnitCost)).ShouldBe(3500m);
        result.Lots.Where(l => l.IsOpen).Select(l => (l.Qty, l.UnitCost)).ShouldBe([(100L, 80m)]);
        result.OpenCost().ShouldBe(8000m);
        result.RealisedPnl.ShouldBe(4000m);
        result.CostNote.ShouldBeNull();
    }

    [Fact]
    public void The_opening_cost_follows_from_the_average_when_the_sales_use_part_of_the_opening_lot()
    {
        // Q = 100 at A = 50; P0 = 60; 10 sold, 50 bought at 99: c0 = (50 x 110 - 4,950) / 60 = 9.1667.
        var result = Run(100, 50m, [Sell("2024-03-01", 10, 120m), Buy("2024-04-01", 50, 99m)]);

        result.OpeningQty.ShouldBe(60);
        result.Lots.Where(l => l.Source == LotSource.Import).ShouldAllBe(l => l.UnitCost == 9.1667m);
        result.RealisedPnl.ShouldBe(10 * (120m - 9.1667m));
        result.Lots.Where(l => l.IsOpen).Select(l => (l.Source, l.Qty)).ShouldBe([(LotSource.Import, 50L), (LotSource.Trade, 50L)]);
    }

    [Fact]
    public void With_no_sales_the_open_lots_cost_what_the_broker_says()
    {
        var result = Run(100, 50m, [Buy("2024-03-05", 20, 60m), Buy("2024-04-01", 5, 70.5m)]);

        result.OpenCost().ShouldBe(100 * 50m, 0.01m);
    }

    [Fact]
    public void Without_an_opening_lot_there_is_nothing_to_cost_and_no_note()
    {
        var result = Run(15, 99m, [Buy("2024-03-05", 10, 90m), Buy("2024-06-12", 5, 110m)]);

        result.Lots.ShouldAllBe(l => l.Source == LotSource.Trade);
        result.CostNote.ShouldBeNull();
        result.OpenCost().ShouldBe(1450m);
    }

    [Fact]
    public void When_the_trades_cost_more_than_the_average_allows_the_opening_lot_costs_the_average_and_says_so()
    {
        var result = Run(20, 10m, [Buy("2024-03-05", 5, 100m)]);

        result.CostNote.ShouldBe(CostNotes.OpeningCostFallback);
        result.Lots.Single(l => l.Source == LotSource.Import).UnitCost.ShouldBe(10m);
    }

    [Fact]
    public void A_split_converts_a_trade_before_the_ex_date_by_the_factor_in_the_direction_of_the_bar_adjustment()
    {
        var split = Split("2024-06-01", 1, 5);
        var factors = AdjustmentFactors.From([split]);

        var result = Run(50, 200m, [Buy("2024-03-01", 10, 1000m)], [split]);

        var lot = result.Lots.ShouldHaveSingleItem();
        (lot.Qty, lot.UnitCost).ShouldBe((50L, 200m));
        result.AdjustedFor.ShouldBe(1);
        // The same direction as the bars: the price is multiplied by FactorAt(date), the quantity divided by it.
        lot.UnitCost.ShouldBe((decimal)AdjustmentFactors.Price(1000d, factors.FactorAt(D("2024-03-01"))));
        lot.Qty.ShouldBe((long)Math.Round(10 / factors.FactorAt(D("2024-03-01"))));
    }

    [Theory]
    [InlineData("2024-06-01", true)]
    [InlineData("2024-06-02", true)]
    [InlineData("2024-05-31", false)]
    public void A_trade_on_the_ex_date_or_later_is_already_on_the_new_basis(string tradeDate, bool onNewBasis)
    {
        var result = Run(onNewBasis ? 10 : 50, 100m, [Buy(tradeDate, 10, 100m)], [Split("2024-06-01", 1, 5)]);

        result.Applicable.ShouldBeTrue();
        result.Lots.ShouldHaveSingleItem().Qty.ShouldBe(onNewBasis ? 10 : 50);
        result.AdjustedFor.ShouldBe(onNewBasis ? 0 : 1);
    }

    [Fact]
    public void A_split_and_a_bonus_multiply_and_each_trade_gets_the_actions_after_its_own_date()
    {
        var actions = new[] { Split("2023-01-01", 1, 2), Bonus("2024-01-01", 1, 1) };
        var factors = AdjustmentFactors.From(actions);

        var result = Run(10 * 4 + 5 * 2 + 3, 1m, [Buy("2022-06-01", 10, 400m), Buy("2023-06-01", 5, 100m), Buy("2024-06-01", 3, 60m)], actions);

        result.Lots.Select(l => (l.Qty, l.UnitCost)).ShouldBe([(40L, 100m), (10L, 50m), (3L, 60m)]);
        result.AdjustedFor.ShouldBe(2);
        result.Net.ShouldBe(53);
        factors.FactorAt(D("2022-06-01")).ShouldBe(0.25, 1e-12);
        factors.FactorAt(D("2023-06-01")).ShouldBe(0.5, 1e-12);
    }

    [Fact]
    public void The_quantity_of_a_bonus_is_exact_not_taken_through_the_rounded_factor()
    {
        var result = Run(300, 10m, [Buy("2024-01-01", 200, 15m)], [Bonus("2024-06-01", 1, 2)]);

        result.Applicable.ShouldBeTrue();
        result.Lots.Single(l => l.Source == LotSource.Trade).Qty.ShouldBe(300);
    }

    [Fact]
    public void A_quantity_that_is_not_whole_after_a_bonus_is_a_mismatch_with_its_own_reason()
    {
        var result = Run(10, 10m, [Buy("2024-01-01", 3, 15m)], [Bonus("2024-06-01", 1, 2)]);

        (result.Applicable, result.Reason, result.Net, result.OpeningQty).ShouldBe((false, MismatchReasons.FractionalAfterAdjustment, null, null));
        result.AdjustedFor.ShouldBe(1);
    }

    [Fact]
    public void A_sale_is_converted_like_a_purchase()
    {
        var result = Run(30, 100m, [Buy("2024-01-01", 10, 1000m), Sell("2024-02-01", 4, 1100m)], [Split("2024-06-01", 1, 10)]);

        // 100 shares bought and 40 sold, as today's shares: the broker's 30 leave 70 as opening.
        result.Net.ShouldBe(60);
        result.OpeningQty.ShouldBe(-30);
        result.Applicable.ShouldBeFalse();
    }

    [Fact]
    public void The_same_trades_in_any_stored_order_give_the_same_lots()
    {
        var trades = new[]
        {
            Buy("2024-02-01", 10, 110m, seq: 1),
            Buy("2024-02-01", 4, 111m, "10:00", seq: 2),
            Sell("2024-02-01", 3, 115m, seq: 3),
            Buy("2024-03-01", 6, 120m, seq: 4),
            Sell("2024-04-01", 12, 130m, seq: 5),
        };

        var first = Run(40, 100m, trades);
        var again = Run(40, 100m, trades.Reverse());
        var third = Run(40, 100m, trades.OrderBy(t => t.Id));

        again.Lots.ShouldBe(first.Lots);
        third.Lots.ShouldBe(first.Lots);
        again.RealisedPnl.ShouldBe(first.RealisedPnl);
    }

    [Fact]
    public void Random_histories_keep_the_figures_the_quantities_the_cost_without_sales_and_a_consistent_realised_profit()
    {
        var random = new Random(23);
        var withSales = 0;
        var withFallback = 0;
        for (var round = 0; round < 400; round++)
        {
            var trades = new List<ReconTrade>();
            var held = random.Next(0, 50);
            var opening = held;
            var day = D("2024-01-01");
            for (var i = 0; i < random.Next(1, 12); i++)
            {
                day = day.AddDays(random.Next(0, 5));
                var qty = random.Next(1, 20);
                var date = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                if (random.Next(2) == 0 || held < qty)
                {
                    trades.Add(Buy(date, qty, 50 + random.Next(0, 100), seq: i));
                    held += qty;
                }
                else
                {
                    trades.Add(Sell(date, qty, 50 + random.Next(0, 100), seq: i));
                    held -= qty;
                }
            }

            var average = 60m + random.Next(0, 40);
            var result = Run(held, average, trades);

            result.Applicable.ShouldBeTrue($"round {round}: {result.Reason}");
            result.OpenQty().ShouldBe(held, $"round {round}");
            result.OpeningQty.ShouldBe(opening, $"round {round}");

            // Realised profit: the sum over the closed pieces, and the pieces add up to what was sold.
            var pieces = result.Lots.Where(l => !l.IsOpen).ToList();
            result.RealisedPnl.ShouldBe(pieces.Sum(p => p.Qty * (p.ClosePrice!.Value - p.UnitCost)), $"round {round}");
            pieces.Sum(p => p.Qty).ShouldBe(trades.Where(t => t.Side == XDrishti.Domain.Portfolio.TradeSide.Sell).Sum(t => t.Qty), $"round {round}");

            var openingLots = result.Lots.Where(l => l.Source == LotSource.Import).ToList();
            if (result.CostNote is not null)
            {
                withFallback++;
                result.CostNote.ShouldBe(CostNotes.OpeningCostFallback);
                openingLots.ShouldAllBe(l => l.UnitCost == average, $"round {round}");
            }
            else if (openingLots.Count > 0)
            {
                openingLots.ShouldAllBe(l => l.UnitCost > 0m, $"round {round}");
            }

            if (!trades.Any(t => t.Side == XDrishti.Domain.Portfolio.TradeSide.Sell))
            {
                // No sales and an opening lot to take the difference: the open lots cost the broker's total, to within the rounding of the opening lot's unit cost to 4 decimals.
                if (result.CostNote is null && opening > 0)
                {
                    result.OpenCost().ShouldBe(held * average, (opening * 0.0001m) + 0.01m, $"round {round}");
                }
            }
            else
            {
                withSales++;
            }
        }

        // The generator reaches every branch: histories with sales, and ones where the trades cost more than the average allows.
        withSales.ShouldBeGreaterThan(50);
        withFallback.ShouldBeGreaterThan(5);
    }
}
