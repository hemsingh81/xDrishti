using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

public sealed class HoldingLotDateTests
{
    [Fact]
    public void A_date_in_the_valid_range_is_stored_and_the_lot_stops_using_the_default_date()
    {
        var holding = HoldingData.Imported(usesDefaultDate: true);
        var lot = holding.Lots.Single();

        var result = holding.ChangeLotDate(lot.Id, new DateOnly(2023, 6, 15), HoldingData.Today);

        result.IsSuccess.ShouldBeTrue();
        (lot.AcquiredOn, lot.UsesDefaultDate).ShouldBe((new DateOnly(2023, 6, 15), false));
    }

    [Theory]
    [InlineData("1990-01-01", true)]
    [InlineData("1989-12-31", false)]
    [InlineData("2026-10-10", true)] // today in India
    [InlineData("2026-10-11", false)]
    public void Dates_must_lie_between_1990_01_01_and_today(string date, bool valid)
    {
        var holding = HoldingData.Imported(usesDefaultDate: true);

        var result = holding.ChangeLotDate(holding.Lots.Single().Id, DateOnly.Parse(date), HoldingData.Today);

        result.IsSuccess.ShouldBe(valid);
        if (!valid)
        {
            result.Error.Code.ShouldBe(PortfolioErrors.InvalidCode);
            result.Error.Fields!.Single().Field.ShouldBe("acquiredOn");
            holding.Lots.Single().UsesDefaultDate.ShouldBeTrue();
        }
    }

    [Fact]
    public void A_closed_lot_cannot_be_dated_after_the_day_it_was_closed()
    {
        var holding = HoldingData.Imported();
        holding.CloseMissing(new DateOnly(2026, 6, 1), HoldingData.T0);

        var result = holding.ChangeLotDate(holding.Lots.Single().Id, new DateOnly(2026, 6, 2), HoldingData.Today);

        result.Error.Code.ShouldBe(PortfolioErrors.InvalidCode);
        holding.ChangeLotDate(holding.Lots.Single().Id, new DateOnly(2026, 6, 1), HoldingData.Today).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_unknown_lot_is_not_found()
    {
        var holding = HoldingData.Imported();

        var result = holding.ChangeLotDate(Guid.NewGuid(), HoldingData.Today, HoldingData.Today);

        result.Error.ShouldBe(PortfolioErrors.LotNotFound);
    }
}
