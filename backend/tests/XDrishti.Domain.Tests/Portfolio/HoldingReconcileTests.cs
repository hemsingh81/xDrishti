using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>The sync rules of ADR 0012: new, increased, decreased, closed and unchanged holdings.</summary>
public sealed class HoldingReconcileTests
{
    private static readonly DateTimeOffset T1 = HoldingData.T0.AddDays(2);

    private static readonly DateOnly Day1 = HoldingData.Today.AddDays(2);

    [Fact]
    public void An_imported_holding_has_one_lot_with_the_brokers_quantity_and_average_cost_on_the_chosen_date()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 120, avgCost: 41.5m, price: 55m), new DateOnly(2024, 3, 4), usesDefaultDate: true);

        var lot = holding.Lots.ShouldHaveSingleItem();
        (lot.AcquiredOn, lot.Qty, lot.UnitCost, lot.Source, lot.UsesDefaultDate, lot.IsOpen).ShouldBe((new DateOnly(2024, 3, 4), 120L, 41.5m, LotSource.Import, true, true));
        (holding.Qty, holding.AvgCost, holding.LastPrice, holding.Status, holding.IsMapped).ShouldBe((120L, 41.5m, 55m, HoldingStatus.Open, false));
        holding.LastPriceAt.ShouldBe(HoldingData.T0);
    }

    [Fact]
    public void The_same_quantity_only_updates_prices_and_the_brokers_average_cost()
    {
        var holding = HoldingData.Imported();

        var change = holding.Reconcile(HoldingData.Row(qty: 100, avgCost: 52m, price: 70m), null, null, Day1, T1);

        change.ShouldBe(HoldingChange.Unchanged);
        holding.Lots.ShouldHaveSingleItem().UnitCost.ShouldBe(50m);
        (holding.AvgCost, holding.LastPrice, holding.LastPriceAt, holding.SyncedAt).ShouldBe((52m, 70m, T1, T1));
    }

    [Fact]
    public void Running_the_same_sync_twice_changes_nothing_the_second_time()
    {
        var holding = HoldingData.Imported();
        var row = HoldingData.Row(qty: 130, avgCost: 53m, price: 70m);
        holding.Reconcile(row, null, null, Day1, T1);
        var lots = holding.Lots.Select(l => (l.AcquiredOn, l.Qty, l.UnitCost, l.ClosedOn)).ToList();

        var change = holding.Reconcile(row, null, null, Day1, T1.AddMinutes(5));

        change.ShouldBe(HoldingChange.Unchanged);
        holding.Lots.Select(l => (l.AcquiredOn, l.Qty, l.UnitCost, l.ClosedOn)).ShouldBe(lots);
    }

    [Fact]
    public void A_larger_quantity_adds_a_lot_dated_today_at_the_implied_cost_of_the_added_shares()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m));

        // 130 shares at an average of 53 cost 6,890; the first 100 cost 5,000: the added 30 cost 1,890, i.e. 63 each.
        var change = holding.Reconcile(HoldingData.Row(qty: 130, avgCost: 53m, price: 70m), null, null, Day1, T1);

        change.ShouldBe(HoldingChange.Increased);
        var added = holding.Lots.Single(l => l.Source == LotSource.Added);
        (added.AcquiredOn, added.Qty, added.UnitCost, added.UsesDefaultDate).ShouldBe((Day1, 30L, 63m, false));
        (holding.Qty, holding.AvgCost).ShouldBe((130L, 53m));
        holding.Lots.Where(l => l.IsOpen).Sum(l => l.Qty).ShouldBe(130L);
    }

    [Theory]
    [InlineData(40)] // implied = (110 * 40 - 100 * 50) / 10 = -60: negative
    [InlineData(45.4545)] // implied = (110 * 45.4545 - 5,000) / 10 = 0.000: not positive
    public void When_the_implied_cost_is_not_positive_the_added_lot_costs_the_last_price(double newAvg)
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m));

        holding.Reconcile(HoldingData.Row(qty: 110, avgCost: (decimal)newAvg, price: 58m), null, null, Day1, T1);

        holding.Lots.Single(l => l.Source == LotSource.Added).UnitCost.ShouldBe(58m);
    }

    [Fact]
    public void Without_a_usable_last_price_the_fallback_is_the_new_average_cost()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m, price: 0m));

        holding.Reconcile(HoldingData.Row(qty: 110, avgCost: 40m, price: 0m), null, null, Day1, T1);

        holding.Lots.Single(l => l.Source == LotSource.Added).UnitCost.ShouldBe(40m);
    }

    [Fact]
    public void A_smaller_quantity_closes_the_oldest_lot_first_at_the_last_price_marked_estimated()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m), new DateOnly(2025, 1, 1));
        holding.Reconcile(HoldingData.Row(qty: 150, avgCost: 60m, price: 80m), null, null, Day1, T1); // adds 50 at 80
        var third = T1.AddDays(1);

        var change = holding.Reconcile(HoldingData.Row(qty: 120, avgCost: 55m, price: 90m), null, null, Day1.AddDays(1), third);

        change.ShouldBe(HoldingChange.Decreased);
        var closed = holding.Lots.Where(l => !l.IsOpen).ToList();
        var piece = closed.ShouldHaveSingleItem();
        (piece.AcquiredOn, piece.Qty, piece.UnitCost, piece.ClosedOn, piece.ClosePrice, piece.Estimated)
            .ShouldBe((new DateOnly(2025, 1, 1), 30L, 50m, Day1.AddDays(1), 90m, true));
        var remainder = holding.OpenLotsOldestFirst().First();
        (remainder.AcquiredOn, remainder.Qty).ShouldBe((new DateOnly(2025, 1, 1), 70L));
        holding.Lots.Where(l => l.IsOpen).Sum(l => l.Qty).ShouldBe(120L);
        holding.Qty.ShouldBe(120L);
    }

    [Fact]
    public void A_sale_that_is_larger_than_the_oldest_lot_continues_with_the_next_one()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m), new DateOnly(2025, 1, 1));
        holding.Reconcile(HoldingData.Row(qty: 150, avgCost: 60m, price: 80m), null, null, Day1, T1);

        holding.Reconcile(HoldingData.Row(qty: 30, avgCost: 70m, price: 90m), null, null, Day1.AddDays(1), T1.AddDays(1));

        holding.Lots.Where(l => !l.IsOpen).Select(l => (l.AcquiredOn, l.Qty)).ShouldBe([(new DateOnly(2025, 1, 1), 100L), (Day1, 20L)], ignoreOrder: true);
        var open = holding.Lots.Single(l => l.IsOpen);
        (open.AcquiredOn, open.Qty, open.UnitCost).ShouldBe((Day1, 30L, 80m));
    }

    [Fact]
    public void A_whole_lot_that_is_sold_is_closed_in_place_without_splitting()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m));
        holding.Reconcile(HoldingData.Row(qty: 130, avgCost: 53m), null, null, Day1, T1);
        var lotCount = holding.Lots.Count;

        holding.Reconcile(HoldingData.Row(qty: 30, avgCost: 63m, price: 66m), null, null, Day1.AddDays(1), T1.AddDays(1));

        holding.Lots.Count.ShouldBe(lotCount);
        holding.Lots.Single(l => !l.IsOpen).Qty.ShouldBe(100L);
    }

    [Fact]
    public void A_holding_the_broker_no_longer_lists_is_closed_at_the_last_known_price()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m, price: 64m));

        var closed = holding.CloseMissing(Day1, T1);

        closed.ShouldBeTrue();
        (holding.Status, holding.Qty, holding.ClosedAt).ShouldBe((HoldingStatus.Closed, 0L, T1));
        var lot = holding.Lots.ShouldHaveSingleItem();
        (lot.IsOpen, lot.ClosePrice, lot.ClosedOn, lot.Estimated).ShouldBe((false, 64m, Day1, true));
        holding.CloseMissing(Day1, T1.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void A_closed_holding_that_is_listed_again_comes_back_with_a_lot_dated_today()
    {
        var holding = HoldingData.Imported();
        holding.CloseMissing(Day1, T1);

        var change = holding.Reconcile(HoldingData.Row(qty: 40, avgCost: 70m, price: 72m), null, null, Day1.AddDays(3), T1.AddDays(3));

        change.ShouldBe(HoldingChange.Added);
        (holding.Status, holding.ClosedAt, holding.Qty).ShouldBe((HoldingStatus.Open, (DateTimeOffset?)null, 40L));
        var lot = holding.Lots.Single(l => l.IsOpen);
        (lot.AcquiredOn, lot.Qty, lot.UnitCost, lot.Source).ShouldBe((Day1.AddDays(3), 40L, 70m, LotSource.Added));
        holding.Lots.Count.ShouldBe(2);
    }

    [Fact]
    public void A_price_of_zero_keeps_the_last_known_price()
    {
        var holding = HoldingData.Imported(HoldingData.Row(price: 64m));

        holding.Reconcile(HoldingData.Row(price: 0m), null, null, Day1, T1);

        (holding.LastPrice, holding.LastPriceAt).ShouldBe((64m, HoldingData.T0));
    }

    [Fact]
    public void The_instrument_and_names_follow_the_master_list()
    {
        var holding = HoldingData.Imported();
        var instrument = Guid.NewGuid();

        holding.Reconcile(HoldingData.Row(), instrument, "Example Industries", Day1, T1);

        (holding.InstrumentId, holding.IsMapped, holding.Name, holding.TradingSymbol, holding.Isin).ShouldBe((instrument, true, "Example Industries", "EXMPL", "INE000A01011"));
    }

    [Fact]
    public void A_holding_without_a_name_from_the_master_list_is_called_by_its_symbol()
    {
        var holding = HoldingData.Imported();

        holding.Name.ShouldBe("EXMPL");
    }
}
