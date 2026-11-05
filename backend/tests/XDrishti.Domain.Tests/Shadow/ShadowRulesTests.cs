using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
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

    [Theory]
    [InlineData(2026, 11, 3, 9, 45, 2026, 11, 3, "BUY")] // an intraday cell is decided on the session itself
    [InlineData(2026, 11, 2, 15, 30, 2026, 11, 2, "BUY")] // a daily cell is decided at the close of the day before the session
    [InlineData(2026, 11, 3, 9, 45, 2026, 11, 3, "SELL")]
    public void The_key_built_before_the_session_equals_the_key_of_the_simulated_trade(int y, int mo, int d, int h, int mi, int ky, int kmo, int kd, string side)
    {
        var decided = new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();
        var bar = new Bar(decided - 900, 99, 100.5, 98, 100, 1_000);
        var trade = new SimTrade(
            SimTrade.CurrentSchemaVersion, "ORB", Guid.Empty, "NSE:PAPA", side == "BUY" ? Side.Buy : Side.Sell, Session, decided, Timeframe.M15, bar, 15, true,
            decided, 100.5, 98, 105.5, decided + 3600, 105.5, ExitReason.Target, 1, 0.4, 2.2, FillBasis.Exact, CostBasis.None, null,
            1, 0, 100.5, 105.5, EntryFill.AtLimit, false, 1, 0, null);

        ShadowTradeKey.For("ORB", "NSE:PAPA", side, new DateOnly(ky, kmo, kd)).ShouldBe(trade.Key);
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
    }
}
