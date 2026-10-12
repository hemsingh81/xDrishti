using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>Re-dating the lots that still carry the first import date (ADR 0015).</summary>
public sealed class HoldingRedateTests
{
    private static readonly DateOnly NewDate = new(2024, 4, 1);

    [Fact]
    public void An_open_default_dated_import_lot_takes_the_new_date_and_stays_flagged_as_default()
    {
        var holding = HoldingData.Imported(acquiredOn: new DateOnly(2025, 1, 1), usesDefaultDate: true);

        var result = holding.RedateDefaultLots(NewDate);

        result.ShouldBe(new LotRedating(1, 0));
        var lot = holding.Lots.Single();
        (lot.AcquiredOn, lot.UsesDefaultDate, lot.Source).ShouldBe((NewDate, true, LotSource.Import));
    }

    [Fact]
    public void A_lot_the_owner_dated_is_left_alone_and_not_counted()
    {
        var holding = HoldingData.Imported(acquiredOn: new DateOnly(2023, 5, 5), usesDefaultDate: false);

        var result = holding.RedateDefaultLots(NewDate);

        result.ShouldBe(LotRedating.None);
        holding.Lots.Single().AcquiredOn.ShouldBe(new DateOnly(2023, 5, 5));
    }

    [Fact]
    public void A_lot_whose_date_was_edited_stops_following_the_first_import_date()
    {
        var holding = HoldingData.Imported(acquiredOn: new DateOnly(2025, 1, 1), usesDefaultDate: true);
        holding.ChangeLotDate(holding.Lots.Single().Id, new DateOnly(2022, 2, 2), HoldingData.Today);

        var result = holding.RedateDefaultLots(NewDate);

        result.ShouldBe(LotRedating.None);
        holding.Lots.Single().AcquiredOn.ShouldBe(new DateOnly(2022, 2, 2));
    }

    [Fact]
    public void Lots_found_by_a_refresh_and_manual_lots_are_never_touched()
    {
        var added = HoldingData.Imported(HoldingData.Row(qty: 100), new DateOnly(2025, 1, 1), usesDefaultDate: true);
        added.Reconcile(HoldingData.Row(qty: 130, avgCost: 53m), null, null, HoldingData.Today, HoldingData.T0);
        var manual = Holding.Open(HoldingData.Account, HoldingData.Bucket, null, null, HoldingData.Row(), new DateOnly(2025, 1, 1), usesDefaultDate: true, LotSource.Manual, HoldingData.T0);

        var addedResult = added.RedateDefaultLots(NewDate);
        var manualResult = manual.RedateDefaultLots(NewDate);

        addedResult.ShouldBe(new LotRedating(1, 0)); // only the imported lot
        added.Lots.Single(l => l.Source == LotSource.Added).AcquiredOn.ShouldBe(HoldingData.Today);
        manualResult.ShouldBe(LotRedating.None);
        manual.Lots.Single().AcquiredOn.ShouldBe(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public void A_closed_lot_is_re_dated_when_the_new_date_is_not_after_its_close_day()
    {
        var holding = HoldingData.Imported(acquiredOn: new DateOnly(2025, 1, 1), usesDefaultDate: true);
        holding.CloseMissing(new DateOnly(2026, 6, 1), HoldingData.T0);

        var result = holding.RedateDefaultLots(new DateOnly(2026, 6, 1)); // the close day itself is allowed

        result.ShouldBe(new LotRedating(1, 0));
        holding.Lots.Single().AcquiredOn.ShouldBe(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public void A_closed_lot_whose_close_day_is_before_the_new_date_is_skipped_and_keeps_its_date()
    {
        var holding = HoldingData.Imported(acquiredOn: new DateOnly(2025, 1, 1), usesDefaultDate: true);
        holding.CloseMissing(new DateOnly(2026, 6, 1), HoldingData.T0);

        var result = holding.RedateDefaultLots(new DateOnly(2026, 6, 2));

        result.ShouldBe(new LotRedating(0, 1));
        var lot = holding.Lots.Single();
        (lot.AcquiredOn, lot.UsesDefaultDate).ShouldBe((new DateOnly(2025, 1, 1), true));
    }

    [Fact]
    public void A_partly_sold_holding_re_dates_the_open_remainder_and_skips_the_piece_closed_before_the_new_date()
    {
        var holding = HoldingData.Imported(HoldingData.Row(qty: 100), new DateOnly(2025, 1, 1), usesDefaultDate: true);
        holding.Reconcile(HoldingData.Row(qty: 60), null, null, new DateOnly(2026, 3, 1), HoldingData.T0); // closes a 40-share piece

        var result = holding.RedateDefaultLots(new DateOnly(2026, 4, 1));

        result.ShouldBe(new LotRedating(1, 1));
        holding.Lots.Single(l => l.IsOpen).AcquiredOn.ShouldBe(new DateOnly(2026, 4, 1));
        holding.Lots.Single(l => !l.IsOpen).AcquiredOn.ShouldBe(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public void Re_dating_twice_to_the_same_date_changes_nothing_more()
    {
        var holding = HoldingData.Imported(acquiredOn: new DateOnly(2025, 1, 1), usesDefaultDate: true);
        holding.RedateDefaultLots(NewDate);

        var again = holding.RedateDefaultLots(NewDate);

        again.ShouldBe(new LotRedating(1, 0)); // still default-dated, so counted; the date is the same
        holding.Lots.Single().AcquiredOn.ShouldBe(NewDate);
    }
}
