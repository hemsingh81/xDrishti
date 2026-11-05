using XDrishti.Domain.Shadow;

namespace XDrishti.Domain.Tests.Shadow;

/// <summary>CF-08: the cut-off (09:00 India time, 03:30 UTC, review fix 1), the trade key shared with the paper record, and the fixed words.</summary>
public sealed class ShadowRulesTests
{
    private static readonly DateOnly Session = new(2026, 11, 3);

    [Fact]
    public void The_cut_off_of_a_session_is_0900_india_time_which_is_0330_utc()
    {
        ShadowCutoff.For(Session).ToUniversalTime().ShouldBe(new DateTimeOffset(2026, 11, 3, 3, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_session_is_opened_from_0900_on_and_not_before()
    {
        ShadowCutoff.IsOpened(Session, new DateTimeOffset(2026, 11, 3, 3, 29, 59, TimeSpan.Zero)).ShouldBeFalse(); // 08:59:59 IST
        ShadowCutoff.IsOpened(Session, new DateTimeOffset(2026, 11, 3, 3, 30, 0, TimeSpan.Zero)).ShouldBeTrue(); // 09:00:00 IST
        ShadowCutoff.IsOpened(Session, new DateTimeOffset(2026, 11, 2, 15, 40, 0, TimeSpan.Zero)).ShouldBeFalse(); // the evening before
    }

    [Theory]
    [InlineData("BUY", "ORB|NSE:PAPA|Buy|2026-11-02")]
    [InlineData("SELL", "ORB|NSE:PAPA|Sell|2026-11-02")]
    public void The_trade_key_is_the_simulator_key(string side, string expected)
    {
        ShadowTradeKey.For("ORB", "NSE:PAPA", side, new DateOnly(2026, 11, 2)).ShouldBe(expected);
    }

    [Fact]
    public void A_side_other_than_buy_or_sell_has_no_key()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ShadowTradeKey.For("ORB", "NSE:PAPA", "HOLD", Session));
    }

    [Fact]
    public void The_fixed_words_are_pinned()
    {
        ShadowWording.ModelledFills.ShouldBe("modelled fills; no queue, no latency");
        ShadowWording.SeparateCount.ShouldContain("separate count");
        ShadowWording.SeparateCount.ShouldContain("P10 exit criterion is untouched");
        ShadowWording.PlaceholderRho.ShouldContain("placeholder");
        ShadowWording.GapMeaning.ShouldContain("never back-filled");
    }
}
