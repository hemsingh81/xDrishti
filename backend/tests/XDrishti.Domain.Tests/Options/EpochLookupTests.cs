using XDrishti.Domain.Options;

namespace XDrishti.Domain.Tests.Options;

public sealed class EpochLookupTests
{
    private static readonly MarketEpoch[] Epochs =
    [
        new("weekly_index_one_per_exchange", new DateOnly(2024, 11, 20), "one weekly", null, false),
        new("upfront_premium", new DateOnly(2025, 2, 1), "upfront", null, false),
        new("nifty_expiry_tuesday", new DateOnly(2025, 9, 1), "tuesday", null, false),
        new("index_lot_revision", new DateOnly(2025, 12, 30), "lots", null, false),
        new("stt_fo_raise", new DateOnly(2026, 4, 1), "stt", null, false),
        new("stt_fo_raise", new DateOnly(2027, 4, 1), "stt again", "https://example.invalid/circular", true),
    ];

    [Theory]
    [InlineData("2024-11-19", new string[0])]
    [InlineData("2024-11-20", new[] { "weekly_index_one_per_exchange" })]
    [InlineData("2025-08-31", new[] { "weekly_index_one_per_exchange", "upfront_premium" })]
    [InlineData("2025-09-01", new[] { "weekly_index_one_per_exchange", "upfront_premium", "nifty_expiry_tuesday" })]
    [InlineData("2026-11-01", new[] { "weekly_index_one_per_exchange", "upfront_premium", "nifty_expiry_tuesday", "index_lot_revision", "stt_fo_raise" })]
    public void The_epochs_in_force_are_those_that_started_on_or_before_the_date(string date, string[] codes)
    {
        EpochLookup.InForce(Epochs, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)).Select(e => e.Code).ShouldBe(codes);
    }

    [Fact]
    public void A_code_that_changed_again_is_in_force_with_its_latest_row_only()
    {
        var stt = EpochLookup.InForce(Epochs, new DateOnly(2027, 4, 1)).Where(e => e.Code == "stt_fo_raise").ShouldHaveSingleItem();

        stt.EffectiveFrom.ShouldBe(new DateOnly(2027, 4, 1));
        stt.Confirmed.ShouldBeTrue();
    }
}
