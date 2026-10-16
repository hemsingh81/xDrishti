using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Live;
using static XDrishti.Application.Tests.Live.FeedTestData;

namespace XDrishti.Application.Tests.Live;

/// <summary>A packet becomes a price for the sink through the current active set (P2-10 criterion 5 and ADR 0024 §3).</summary>
public sealed class FeedPacketMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 5, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<FeedSecurity, Guid> Known = new()
    {
        [new FeedSecurity(FeedSegments.NseEquity, 2885)] = Reliance,
        [new FeedSecurity(FeedSegments.Index, 13)] = Nifty,
    };

    [Fact]
    public void A_ticker_of_a_known_security_becomes_a_price_with_its_own_time()
    {
        var at = Now.AddSeconds(-3);

        var mapped = FeedPacketMapper.Map(Tick(FeedSegments.NseEquity, 2885, 2950.5m, at), Known, Now);

        mapped.Outcome.ShouldBe(PacketOutcome.Price);
        mapped.Price.ShouldBe(new LivePrice(Reliance, 2950.5m, at));
    }

    [Fact]
    public void A_quote_packet_gives_its_last_price_too()
    {
        var mapped = FeedPacketMapper.Map(Tick(FeedSegments.Index, 13, 24800m, Now, FeedPacketKind.Quote), Known, Now);

        mapped.Price!.InstrumentId.ShouldBe(Nifty);
    }

    [Fact]
    public void The_same_security_id_in_another_segment_is_a_different_security()
    {
        var mapped = FeedPacketMapper.Map(Tick(FeedSegments.NseEquity, 13, 100m, Now), Known, Now);

        mapped.Outcome.ShouldBe(PacketOutcome.UnknownInstrument);
        mapped.Price.ShouldBeNull();
    }

    [Fact]
    public void A_tick_of_an_instrument_outside_the_set_is_unknown()
    {
        FeedPacketMapper.Map(Tick(FeedSegments.NseEquity, 99999, 100m, Now), Known, Now).Outcome.ShouldBe(PacketOutcome.UnknownInstrument);
    }

    [Fact]
    public void The_previous_close_is_ignored_until_its_meaning_is_checked()
    {
        FeedPacketMapper.Map(Tick(FeedSegments.NseEquity, 2885, 2900m, Now, FeedPacketKind.PreviousClose), Known, Now).Outcome.ShouldBe(PacketOutcome.Ignored);
    }

    [Fact]
    public void A_disconnect_packet_is_not_a_price()
    {
        FeedPacketMapper.Map(new FeedPacket(FeedPacketKind.Disconnect, default, Reason: 807), Known, Now).Outcome.ShouldBe(PacketOutcome.Ignored);
    }

    [Fact]
    public void A_trade_time_ahead_of_the_clock_is_replaced_by_the_clock()
    {
        var mapped = FeedPacketMapper.Map(Tick(FeedSegments.NseEquity, 2885, 2950.5m, Now.AddHours(5.5)), Known, Now);

        mapped.Price!.TickAt.ShouldBe(Now);
    }

    [Fact]
    public void A_trade_time_a_little_ahead_of_the_clock_is_kept()
    {
        var at = Now.AddSeconds(20);

        FeedPacketMapper.Map(Tick(FeedSegments.NseEquity, 2885, 2950.5m, at), Known, Now).Price!.TickAt.ShouldBe(at);
    }

    [Fact]
    public void A_pre_open_price_is_passed_through_for_the_sink_to_judge()
    {
        var preOpen = new DateTimeOffset(2026, 10, 12, 9, 5, 0, TimeSpan.FromMinutes(330));

        var mapped = FeedPacketMapper.Map(Tick(FeedSegments.NseEquity, 2885, 2950.5m, preOpen), Known, preOpen.AddSeconds(2));

        mapped.Outcome.ShouldBe(PacketOutcome.Price);
        mapped.Price!.TickAt.ShouldBe(preOpen);
    }
}
