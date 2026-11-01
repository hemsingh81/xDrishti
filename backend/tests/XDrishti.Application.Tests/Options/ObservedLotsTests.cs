using XDrishti.Application.Abstractions.Options;
using XDrishti.Application.Options.Import;

namespace XDrishti.Application.Tests.Options;

/// <summary>Spec P14-01 criterion 4: the lot the file shows for an underlying on the day.</summary>
public sealed class ObservedLotsTests
{
    private static readonly Guid Ambuja = Guid.Parse("00000000-0000-7000-8000-000000001270");
    private static readonly Guid Nifty = Guid.Parse("00000000-0000-7000-8000-000000000013");
    private static readonly DateOnly Today = new(2026, 11, 1);

    private static OptionContractCandidate C(Guid underlying, string expiry, int lot, long id = 1) =>
        new(id, underlying, "OPTSTK", DateOnly.Parse(expiry, System.Globalization.CultureInfo.InvariantCulture), 100, "CE", "month", lot, 5);

    [Fact]
    public void During_a_revision_the_nearest_unexpired_expirys_lot_is_observed_and_the_underlying_counted_mixed()
    {
        // AMBUJACEM in the real file of 2026-11-01: 1200 for November and December, 1350 from January.
        var (lots, mixed) = ObservedLots.Pick([C(Ambuja, "2027-01-25", 1350), C(Ambuja, "2026-11-23", 1200), C(Ambuja, "2026-12-29", 1200), C(Nifty, "2026-11-09", 65)], Today);

        lots.ShouldBe([new LotObservation(Nifty, 65), new LotObservation(Ambuja, 1200)], ignoreOrder: true);
        mixed.ShouldBe(1);
    }

    [Fact]
    public void Expired_rows_are_ignored_and_an_underlying_with_only_expired_rows_shows_no_lot()
    {
        var (lots, _) = ObservedLots.Pick([C(Nifty, "2025-12-23", 75), C(Nifty, "2026-11-09", 65), C(Ambuja, "2026-10-27", 1200)], Today);

        lots.ShouldBe([new LotObservation(Nifty, 65)]);
    }

    [Fact]
    public void An_expiry_on_the_day_itself_still_counts()
    {
        ObservedLots.Pick([C(Nifty, "2026-11-01", 75), C(Nifty, "2026-11-09", 65)], Today).Lots.ShouldBe([new LotObservation(Nifty, 75)]);
    }

    [Fact]
    public void Two_lots_on_the_nearest_expiry_give_the_more_common_then_the_smaller()
    {
        ObservedLots.Pick([C(Nifty, "2026-11-09", 75, 1), C(Nifty, "2026-11-09", 65, 2), C(Nifty, "2026-11-09", 65, 3)], Today).Lots.Single().LotSize.ShouldBe(65);
        ObservedLots.Pick([C(Nifty, "2026-11-09", 75, 1), C(Nifty, "2026-11-09", 65, 2)], Today).Lots.Single().LotSize.ShouldBe(65);
    }
}
