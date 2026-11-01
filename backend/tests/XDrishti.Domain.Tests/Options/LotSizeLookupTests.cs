using XDrishti.Domain.Options;

namespace XDrishti.Domain.Tests.Options;

public sealed class LotSizeLookupTests
{
    private static readonly Guid Nifty = Guid.Parse("00000000-0000-7000-8000-000000000013");
    private static readonly Guid BankNifty = Guid.Parse("00000000-0000-7000-8000-000000000025");

    private static readonly LotSizeEntry[] Seeds =
    [
        new(1, Nifty, new DateOnly(2024, 11, 20), 75, LotSizeEntry.Secondary, false),
        new(2, Nifty, new DateOnly(2025, 12, 30), 65, LotSizeEntry.Secondary, false),
        new(3, BankNifty, new DateOnly(2025, 12, 30), 30, LotSizeEntry.Secondary, false),
    ];

    [Theory]
    [InlineData("2024-11-20", 75)]
    [InlineData("2025-12-29", 75)]
    [InlineData("2025-12-30", 65)]
    [InlineData("2026-11-01", 65)]
    public void The_lot_in_force_is_the_latest_row_on_or_before_the_date(string date, int expected)
    {
        var lot = LotSizeLookup.InForce(Seeds, Nifty, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));

        lot.Available.ShouldBeTrue();
        lot.LotSize.ShouldBe(expected);
        lot.Confirmed.ShouldBeFalse();
        lot.Source.ShouldBe(LotSizeEntry.Secondary);
    }

    [Fact]
    public void Before_the_first_row_or_for_an_underlying_without_rows_it_is_unavailable()
    {
        LotSizeLookup.InForce(Seeds, Nifty, new DateOnly(2024, 11, 19)).ShouldBe(LotInForce.Unavailable);
        LotSizeLookup.InForce(Seeds, BankNifty, new DateOnly(2025, 12, 29)).Available.ShouldBeFalse();
        LotSizeLookup.InForce(Seeds, Guid.NewGuid(), new DateOnly(2026, 1, 1)).Available.ShouldBeFalse();
    }

    [Fact]
    public void On_the_same_date_a_confirmed_row_wins_then_the_one_recorded_last()
    {
        var day = new DateOnly(2026, 6, 1);
        LotSizeEntry[] rows =
        [
            new(10, Nifty, day, 70, LotSizeEntry.ScripMaster, false),
            new(11, Nifty, day, 72, LotSizeEntry.Circular, true),
            new(12, Nifty, day, 74, LotSizeEntry.ScripMaster, false),
        ];

        LotSizeLookup.InForce(rows, Nifty, day).ShouldBe(new LotInForce(true, 72, true, day, LotSizeEntry.Circular));
        LotSizeLookup.InForce(rows.Where(r => !r.Confirmed), Nifty, day).LotSize.ShouldBe(74);
    }

    [Fact]
    public void The_seeds_are_all_unconfirmed_secondary_rows_with_their_address()
    {
        LotSizeSeed.All.ShouldAllBe(s => !s.Confirmed && s.Source == LotSizeEntry.Secondary && s.SourceUrl.StartsWith("https://", StringComparison.Ordinal));
        LotSizeSeed.All.Where(s => s.UnderlyingSymbol == "NSE:NIFTY").Select(s => (s.EffectiveFrom, s.LotSize))
            .ShouldBe([(new DateOnly(2024, 11, 20), 75), (new DateOnly(2025, 12, 30), 65)]);
    }
}
