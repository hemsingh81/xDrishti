using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>A holding whose lots were rebuilt from its trades (ADR 0023): replacing the lots, the lot dates and the re-dating.</summary>
public sealed class HoldingTradeLotsTests
{
    private static readonly DateOnly Opening = new(2024, 3, 4);

    private static readonly DateOnly FirstTrade = new(2024, 3, 5);

    private static LotPlan[] Plan() =>
    [
        new LotPlan(Opening, 60, 52m, LotSource.Import, UsesDefaultDate: true),
        new LotPlan(FirstTrade, 40, 48m, LotSource.Trade, UsesDefaultDate: false),
        new LotPlan(Opening, 5, 52m, LotSource.Import, UsesDefaultDate: true, new DateOnly(2024, 9, 20), 70m),
    ];

    private static Holding Rebuilt()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m));
        holding.ReplaceLots(Plan(), HoldingData.T0);
        return holding;
    }

    [Fact]
    public void Replacing_the_lots_removes_the_old_ones_and_makes_the_planned_ones_in_order_with_real_close_prices()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m));
        var old = holding.Lots.Single().Id;

        holding.ReplaceLots(Plan(), HoldingData.T0);

        holding.Lots.Select(l => l.Id).ShouldNotContain(old);
        holding.Lots.Select(l => (l.AcquiredOn, l.Qty, l.UnitCost, l.Source, l.UsesDefaultDate, l.ClosedOn, l.ClosePrice, l.Estimated)).ShouldBe(
        [
            (Opening, 60L, 52m, LotSource.Import, true, null, null, false),
            (FirstTrade, 40L, 48m, LotSource.Trade, false, null, null, false),
            (Opening, 5L, 52m, LotSource.Import, true, new DateOnly(2024, 9, 20), 70m, false),
        ]);
        holding.Lots.Select(l => l.CreatedAt).ShouldBeInOrder();
        holding.Lots.Select(l => l.CreatedAt).Distinct().Count().ShouldBe(3);
        (holding.Qty, holding.AvgCost).ShouldBe((100L, 50m));
    }

    [Fact]
    public void The_open_quantities_of_the_plan_must_add_up_to_the_brokers_quantity()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m));

        Should.Throw<ArgumentException>(() => holding.ReplaceLots([new LotPlan(Opening, 99, 52m, LotSource.Import, true)], HoldingData.T0));

        holding.Lots.Single().Qty.ShouldBe(100);
    }

    [Fact]
    public void A_piece_cannot_be_closed_before_it_was_acquired_and_a_closed_holding_is_not_rebuilt()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 100, avgCost: 50m));
        var early = new LotPlan(FirstTrade, 5, 52m, LotSource.Trade, false, Opening, 70m);

        Should.Throw<ArgumentException>(() => holding.ReplaceLots([new LotPlan(Opening, 100, 50m, LotSource.Import, true), early], HoldingData.T0));
        holding.CloseMissing(HoldingData.Today, HoldingData.T0);
        Should.Throw<InvalidOperationException>(() => holding.ReplaceLots([], HoldingData.T0));
    }

    [Fact]
    public void A_holding_is_rebuilt_from_trades_when_a_lot_comes_from_a_trade_or_a_piece_was_really_sold()
    {
        var plain = HoldingData.Imported();
        var closedByASync = HoldingData.Imported(HoldingData.Row(qty: 100));
        closedByASync.Reconcile(HoldingData.Row(qty: 90), null, null, HoldingData.Today, HoldingData.T0);

        Rebuilt().HasTradeLots.ShouldBeTrue();
        plain.HasTradeLots.ShouldBeFalse();
        closedByASync.HasTradeLots.ShouldBeFalse();
        Rebuilt().EarliestTradeDate.ShouldBe(FirstTrade);
        plain.EarliestTradeDate.ShouldBeNull();
    }

    [Fact]
    public void A_sale_before_the_first_buy_is_the_earliest_trade()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 6, avgCost: 50m));

        holding.ReplaceLots(
        [
            new LotPlan(new DateOnly(2024, 1, 31), 6, 50m, LotSource.Import, true),
            new LotPlan(new DateOnly(2024, 1, 31), 4, 50m, LotSource.Import, true, new DateOnly(2024, 2, 1), 60m),
        ], HoldingData.T0);

        holding.EarliestTradeDate.ShouldBe(new DateOnly(2024, 2, 1));
    }

    [Fact]
    public void The_date_of_a_trade_lot_cannot_be_changed()
    {
        var holding = Rebuilt();
        var lot = holding.Lots.Single(l => l.Source == LotSource.Trade);

        var result = holding.ChangeLotDate(lot.Id, new DateOnly(2024, 1, 1), HoldingData.Today);

        (result.IsFailure, result.Error.Code).ShouldBe((true, "portfolio.lot_from_trade"));
        lot.AcquiredOn.ShouldBe(FirstTrade);
    }

    [Theory]
    [InlineData("2024-03-05", false)]
    [InlineData("2024-03-06", false)]
    [InlineData("2024-03-04", true)]
    [InlineData("2020-01-01", true)]
    public void The_opening_lot_can_only_go_before_the_earliest_trade(string date, bool allowed)
    {
        var holding = Rebuilt();
        var opening = holding.Lots.First(l => l.Source == LotSource.Import && l.IsOpen);

        var result = holding.ChangeLotDate(opening.Id, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), HoldingData.Today);

        result.IsSuccess.ShouldBe(allowed);
        if (!allowed)
        {
            result.Error.Fields!.Single().Field.ShouldBe("acquiredOn");
        }
    }

    [Fact]
    public void Re_dating_the_default_lots_leaves_a_holding_rebuilt_from_trades_alone_and_counts_its_default_lots_as_skipped()
    {
        var holding = Rebuilt();
        var dates = holding.Lots.Select(l => l.AcquiredOn).ToList();

        var result = holding.RedateDefaultLots(new DateOnly(2024, 4, 1));

        result.ShouldBe(new LotRedating(0, 2));
        holding.Lots.Select(l => l.AcquiredOn).ShouldBe(dates);
    }

    [Fact]
    public void Re_dating_a_holding_without_trade_lots_works_as_before()
    {
        var holding = HoldingData.Imported(acquiredOn: new DateOnly(2025, 1, 1), usesDefaultDate: true);

        holding.RedateDefaultLots(new DateOnly(2024, 4, 1)).ShouldBe(new LotRedating(1, 0));
    }
}
