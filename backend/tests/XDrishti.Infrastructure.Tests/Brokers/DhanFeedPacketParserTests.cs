using System.Buffers.Binary;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>
/// The packets of Dhan's market feed (ADR 0024, P2-10 criterion 3), built from the documented layout: header, ticker, quote, previous close, disconnect, and everything that is dropped. The fixtures of docs/research/dhan-samples are constructed
/// from the same documentation and stay UNVERIFIED until the owner's first real session.
/// </summary>
public sealed class DhanFeedPacketParserTests
{
    private const byte Index = 0;

    private const byte Equity = 1;

    private static readonly DateTimeOffset Now = FeedPackets.FixtureTime.AddSeconds(5);

    private readonly DhanFeedPacketParser _parser = new(new FakeTimeProvider(Now));

    private FeedPacket? Parse(byte[] message, out FeedDropReason reason)
    {
        return _parser.TryParse(message, out var packet, out reason) ? packet : null;
    }

    [Fact]
    public void A_ticker_packet_has_the_security_the_last_price_and_the_trade_time()
    {
        var packet = Parse(FeedPackets.Ticker(Equity, 1333, 1650.25f, FeedPackets.Epoch(FeedPackets.FixtureTime)), out var reason);

        reason.ShouldBe(FeedDropReason.None);
        packet.ShouldBe(new FeedPacket(FeedPacketKind.Ticker, new FeedSecurity(FeedSegments.NseEquity, 1333), 1650.25m, FeedPackets.FixtureTime));
    }

    [Fact]
    public void A_large_price_keeps_its_digits_the_float_is_not_cut_to_seven_significant_digits()
    {
        var packet = Parse(FeedPackets.Ticker(Index, 13, 140123.45f, FeedPackets.Epoch(FeedPackets.FixtureTime)), out _);

        packet!.Price.ShouldBe(140123.4531m); // the float's own value (140123.453125) to four decimals, not 140123.5
    }

    [Fact]
    public void An_index_ticker_is_in_the_index_segment()
    {
        var packet = Parse(FeedPackets.Ticker(Index, 13, 24800.5f, FeedPackets.Epoch(FeedPackets.FixtureTime)), out _);

        packet!.Security.ShouldBe(new FeedSecurity(FeedSegments.Index, 13));
        packet.Price.ShouldBe(24800.5m);
    }

    [Fact]
    public void A_quote_packet_adds_volume_and_the_days_open_high_low_and_close()
    {
        var packet = Parse(FeedPackets.Quote(Equity, 2885, 2950.5f, FeedPackets.Epoch(FeedPackets.FixtureTime), 1_234_567, 2940f, 2931f, 2955.75f, 2938.5f), out _);

        packet.ShouldBe(new FeedPacket(FeedPacketKind.Quote, new FeedSecurity(FeedSegments.NseEquity, 2885), 2950.5m, FeedPackets.FixtureTime, 1_234_567, 2940m, 2955.75m, 2938.5m, 2931m));
    }

    [Fact]
    public void A_quote_packet_with_zero_open_high_low_close_and_volume_is_still_a_price()
    {
        var packet = Parse(FeedPackets.Quote(Index, 21, 14.35f, FeedPackets.Epoch(FeedPackets.FixtureTime), 0, 0f, 0f, 0f, 0f), out _);

        packet!.Price.ShouldBe(14.35m);
        packet.Volume.ShouldBeNull();
        packet.Open.ShouldBeNull();
        packet.High.ShouldBeNull();
        packet.Low.ShouldBeNull();
        packet.Close.ShouldBeNull();
    }

    [Fact]
    public void A_previous_close_packet_carries_the_close()
    {
        var packet = Parse(FeedPackets.PreviousClose(Equity, 2885, 2931f), out _);

        packet.ShouldBe(new FeedPacket(FeedPacketKind.PreviousClose, new FeedSecurity(FeedSegments.NseEquity, 2885), 2931m));
    }

    [Fact]
    public void A_disconnect_packet_carries_the_reason_code()
    {
        var packet = Parse(FeedPackets.Disconnect(807), out _);

        packet.ShouldNotBeNull().Kind.ShouldBe(FeedPacketKind.Disconnect);
        packet.Reason.ShouldBe(807);
    }

    [Theory]
    [InlineData("feed-ticker.hex", FeedPacketKind.Ticker, FeedSegments.NseEquity, 1333, "1650.25")]
    [InlineData("feed-index-ticker.hex", FeedPacketKind.Ticker, FeedSegments.Index, 13, "24800.5")]
    [InlineData("feed-quote.hex", FeedPacketKind.Quote, FeedSegments.NseEquity, 2885, "2950.5")]
    [InlineData("feed-previous-close.hex", FeedPacketKind.PreviousClose, FeedSegments.NseEquity, 2885, "2931")]
    public void The_documented_fixtures_parse_as_described(string file, FeedPacketKind kind, string segment, int securityId, string price)
    {
        var packet = Parse(FeedPackets.Fixture(file), out var reason);

        reason.ShouldBe(FeedDropReason.None);
        packet.ShouldNotBeNull().Kind.ShouldBe(kind);
        packet.Security.ShouldBe(new FeedSecurity(segment, securityId));
        packet.Price.ShouldBe(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_disconnect_fixture_is_an_expired_token()
    {
        Parse(FeedPackets.Fixture("feed-disconnect.hex"), out _).ShouldNotBeNull().Reason.ShouldBe(807);
        FeedDisconnectReasons.IsTokenProblem(807).ShouldBeTrue();
    }

    [Fact]
    public void The_quote_fixture_has_its_documented_volume_and_ohlc()
    {
        var packet = Parse(FeedPackets.Fixture("feed-quote.hex"), out _)!;

        (packet.Volume, packet.Open, packet.High, packet.Low, packet.Close).ShouldBe((1_234_567, 2940m, 2955.75m, 2938.5m, 2931m));
        packet.TradeTime.ShouldBe(FeedPackets.FixtureTime);
    }

    // ---- everything that is dropped ----

    [Fact]
    public void A_message_shorter_than_the_header_is_dropped()
    {
        Parse([2, 16, 0], out var reason).ShouldBeNull();
        reason.ShouldBe(FeedDropReason.TooShort);
        Parse([], out reason).ShouldBeNull();
        reason.ShouldBe(FeedDropReason.TooShort);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(51)]
    [InlineData(255)]
    public void An_unknown_response_code_is_dropped(byte code)
    {
        var message = FeedPackets.Ticker(Equity, 1333, 100f, FeedPackets.Epoch(FeedPackets.FixtureTime));
        message[0] = code;

        Parse(message, out var reason).ShouldBeNull();
        reason.ShouldBe(FeedDropReason.UnknownCode);
    }

    [Fact]
    public void A_message_shorter_than_its_kind_needs_is_dropped_for_its_length()
    {
        var ticker = FeedPackets.Ticker(Equity, 1333, 100f, FeedPackets.Epoch(FeedPackets.FixtureTime));
        var quote = FeedPackets.Quote(Equity, 1333, 100f, FeedPackets.Epoch(FeedPackets.FixtureTime));

        Parse(ticker[..15], out var first).ShouldBeNull();
        Parse(quote[..49], out var second).ShouldBeNull();
        Parse([.. ticker, 0], out var third).ShouldBeNull();

        new[] { first, second, third }.ShouldAllBe(r => r == FeedDropReason.LengthMismatch);
    }

    [Fact]
    public void A_length_field_that_does_not_match_the_message_is_dropped()
    {
        var message = FeedPackets.Ticker(Equity, 1333, 100f, FeedPackets.Epoch(FeedPackets.FixtureTime));
        BinaryPrimitives.WriteInt16LittleEndian(message.AsSpan(1), 8);

        Parse(message, out var reason).ShouldBeNull();
        reason.ShouldBe(FeedDropReason.LengthMismatch);
    }

    [Theory]
    [InlineData(2)] // NSE_FNO
    [InlineData(4)] // BSE_EQ
    [InlineData(5)] // MCX
    [InlineData(200)]
    public void A_segment_this_application_does_not_carry_is_dropped(byte segment)
    {
        Parse(FeedPackets.Ticker(segment, 1333, 100f, FeedPackets.Epoch(FeedPackets.FixtureTime)), out var reason).ShouldBeNull();
        reason.ShouldBe(FeedDropReason.UnknownSegment);
    }

    [Fact]
    public void A_security_id_that_is_not_positive_is_dropped()
    {
        Parse(FeedPackets.Ticker(Equity, 0, 100f, FeedPackets.Epoch(FeedPackets.FixtureTime)), out var reason).ShouldBeNull();
        reason.ShouldBe(FeedDropReason.UnknownSegment);
        Parse(FeedPackets.Ticker(Equity, -5, 100f, FeedPackets.Epoch(FeedPackets.FixtureTime)), out reason).ShouldBeNull();
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1.5f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(3e9f)]
    public void A_price_that_is_not_a_positive_finite_number_is_dropped(float price)
    {
        var epoch = FeedPackets.Epoch(FeedPackets.FixtureTime);

        Parse(FeedPackets.Ticker(Equity, 1333, price, epoch), out var ticker).ShouldBeNull();
        Parse(FeedPackets.Quote(Equity, 1333, price, epoch), out var quote).ShouldBeNull();
        Parse(FeedPackets.PreviousClose(Equity, 1333, price), out var previous).ShouldBeNull();

        new[] { ticker, quote, previous }.ShouldAllBe(r => r == FeedDropReason.BadPrice);
    }

    [Fact]
    public void A_trade_time_more_than_a_day_from_the_clock_is_dropped()
    {
        var late = FeedPackets.Ticker(Equity, 1333, 100f, FeedPackets.Epoch(Now.AddHours(25)));
        var early = FeedPackets.Ticker(Equity, 1333, 100f, FeedPackets.Epoch(Now.AddHours(-25)));
        var quote = FeedPackets.Quote(Equity, 1333, 100f, FeedPackets.Epoch(Now.AddDays(-30)));

        Parse(late, out var first).ShouldBeNull();
        Parse(early, out var second).ShouldBeNull();
        Parse(quote, out var third).ShouldBeNull();

        new[] { first, second, third }.ShouldAllBe(r => r == FeedDropReason.BadTime);
    }

    [Fact]
    public void A_trade_time_of_zero_is_dropped_but_one_within_a_day_is_kept()
    {
        Parse(FeedPackets.Ticker(Equity, 1333, 100f, 0), out var reason).ShouldBeNull();
        reason.ShouldBe(FeedDropReason.BadTime);

        Parse(FeedPackets.Ticker(Equity, 1333, 100f, FeedPackets.Epoch(Now.AddHours(-23))), out _).ShouldNotBeNull();
        Parse(FeedPackets.Ticker(Equity, 1333, 100f, FeedPackets.Epoch(Now.AddHours(5.5))), out _).ShouldNotBeNull();
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            var message = new byte[random.Next(0, 80)];
            random.NextBytes(message);
            if (message.Length > 0 && random.Next(2) == 0)
            {
                message[0] = (byte)new[] { 2, 4, 6, 50 }[random.Next(4)];
            }

            Should.NotThrow(() => _ = _parser.TryParse(message, out _, out _));
        }
    }

    // ---- a frame holds several packets back to back (observed 2026-10-14) ----

    /// <summary>Walks a frame like the connection does: every packet or drop, in order, with the bytes each used.</summary>
    private List<(FeedPacket? Packet, FeedDropReason Reason, int Used)> Walk(byte[] frame)
    {
        var steps = new List<(FeedPacket?, FeedDropReason, int)>();
        var offset = 0;
        while (offset < frame.Length)
        {
            var used = _parser.ParseNext(frame.AsSpan(offset), out var packet, out var reason, out _);
            used.ShouldBeGreaterThan(0);
            steps.Add((packet, reason, used));
            offset += used;
        }

        return steps;
    }

    private static byte[] Frame(params byte[][] packets) => [.. packets.SelectMany(p => p)];

    [Fact]
    public void The_multi_packet_fixture_of_one_hundred_sixteen_bytes_decodes_as_quote_quote_and_previous_close()
    {
        var frame = FeedPackets.Fixture("feed-multi-packet.hex");

        var steps = Walk(frame);

        frame.Length.ShouldBe(116);
        steps.Select(s => s.Used).ShouldBe([50, 50, 16]);
        steps.Select(s => s.Packet!.Kind).ShouldBe([FeedPacketKind.Quote, FeedPacketKind.Quote, FeedPacketKind.PreviousClose]);
        steps.Select(s => s.Packet!.Security.SecurityId).ShouldBe([2885, 1333, 1594]);
    }

    [Fact]
    public void A_frame_of_several_quotes_and_a_previous_close_decodes_every_packet()
    {
        var epoch = FeedPackets.Epoch(FeedPackets.FixtureTime);
        var frame = Frame(FeedPackets.Quote(1, 1, 10f, epoch), FeedPackets.Quote(1, 2, 20f, epoch), FeedPackets.Quote(0, 13, 24800f, epoch), FeedPackets.Quote(1, 4, 40f, epoch), FeedPackets.PreviousClose(1, 1, 9f), FeedPackets.Ticker(1, 5, 50f, epoch));

        var steps = Walk(frame);

        steps.ShouldAllBe(s => s.Reason == FeedDropReason.None);
        steps.Select(s => s.Packet!.Price).ShouldBe([10m, 20m, 24800m, 40m, 9m, 50m]);
    }

    [Fact]
    public void A_single_packet_frame_is_a_frame_of_one()
    {
        var steps = Walk(FeedPackets.Ticker(1, 1333, 100f, FeedPackets.Epoch(FeedPackets.FixtureTime)));

        steps.ShouldHaveSingleItem().Used.ShouldBe(16);
    }

    [Fact]
    public void A_packet_that_cannot_be_used_in_the_middle_is_dropped_and_the_walk_goes_on_after_it()
    {
        var epoch = FeedPackets.Epoch(FeedPackets.FixtureTime);
        var unknown = FeedPackets.Header(99, 20, 1, 5);
        var frame = Frame(FeedPackets.Ticker(1, 1, 10f, epoch), unknown, FeedPackets.Ticker(1, 0, 0f, epoch), FeedPackets.Ticker(1, 2, 20f, epoch));

        var steps = Walk(frame);

        steps.Select(s => s.Reason).ShouldBe([FeedDropReason.None, FeedDropReason.UnknownCode, FeedDropReason.UnknownSegment, FeedDropReason.None]);
        steps.Select(s => s.Used).ShouldBe([16, 20, 16, 16]);
    }

    [Fact]
    public void A_packet_whose_size_does_not_fit_its_code_is_dropped_whole_and_the_walk_goes_on()
    {
        var epoch = FeedPackets.Epoch(FeedPackets.FixtureTime);
        var tickerOfQuoteSize = FeedPackets.Header(2, 20, 1, 7); // a ticker that declares 20 bytes
        var steps = Walk(Frame(tickerOfQuoteSize, FeedPackets.Ticker(1, 2, 20f, epoch)));

        steps.Select(s => s.Reason).ShouldBe([FeedDropReason.LengthMismatch, FeedDropReason.None]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    public void A_tail_shorter_than_a_header_is_one_drop_for_the_remainder(int tail)
    {
        var frame = Frame(FeedPackets.Ticker(1, 1, 10f, FeedPackets.Epoch(FeedPackets.FixtureTime)), new byte[tail]);

        var steps = Walk(frame);

        steps.Select(s => (s.Reason, s.Used)).ShouldBe([(FeedDropReason.None, 16), (FeedDropReason.TooShort, tail)]);
    }

    [Fact]
    public void A_tail_cut_in_the_middle_of_a_packet_is_one_drop_for_the_remainder()
    {
        var epoch = FeedPackets.Epoch(FeedPackets.FixtureTime);
        var frame = Frame(FeedPackets.Ticker(1, 1, 10f, epoch), FeedPackets.Quote(1, 2, 20f, epoch)[..30]); // the second packet declares 50 but only 30 bytes are there

        var steps = Walk(frame);

        steps.Select(s => (s.Reason, s.Used)).ShouldBe([(FeedDropReason.None, 16), (FeedDropReason.LengthMismatch, 30)]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(-3)]
    [InlineData(40)] // more than is left
    [InlineData(30000)]
    public void A_declared_length_below_a_header_or_beyond_what_is_left_ends_the_walk_with_one_drop(int declared)
    {
        var epoch = FeedPackets.Epoch(FeedPackets.FixtureTime);
        var broken = FeedPackets.Ticker(1, 2, 20f, epoch);
        BinaryPrimitives.WriteInt16LittleEndian(broken.AsSpan(1), (short)declared);
        var frame = Frame(FeedPackets.Ticker(1, 1, 10f, epoch), broken, FeedPackets.Ticker(1, 3, 30f, epoch));

        var steps = Walk(frame);

        steps.Count.ShouldBe(2); // the good one, then everything after it as one drop
        steps[1].Reason.ShouldBe(FeedDropReason.LengthMismatch);
        steps[1].Used.ShouldBe(32);
    }

    [Fact]
    public void An_empty_rest_is_not_walked_and_the_declared_length_is_reported_when_a_header_fits()
    {
        var used = _parser.ParseNext(FeedPackets.Ticker(1, 1, 10f, FeedPackets.Epoch(FeedPackets.FixtureTime)), out _, out _, out var declared);

        (used, declared).ShouldBe((16, 16));
        _parser.ParseNext([1, 2, 3], out _, out _, out var none).ShouldBe(3);
        none.ShouldBeNull();
    }

    [Fact]
    public void Random_frames_never_throw_and_every_step_makes_progress()
    {
        var random = new Random(11);
        var epoch = FeedPackets.Epoch(FeedPackets.FixtureTime);
        for (var i = 0; i < 3000; i++)
        {
            var frame = random.Next(3) == 0
                ? Frame(FeedPackets.Ticker(1, 1, 10f, epoch), FeedPackets.Quote(1, 2, 20f, epoch), new byte[random.Next(0, 40)].Select(_ => (byte)random.Next(256)).ToArray())
                : new byte[random.Next(0, 200)].Select(_ => (byte)random.Next(256)).ToArray();
            var offset = 0;
            Should.NotThrow(() =>
            {
                while (offset < frame.Length)
                {
                    var used = _parser.ParseNext(frame.AsSpan(offset), out _, out _, out _);
                    used.ShouldBeGreaterThan(0);
                    offset += used;
                }
            });
            offset.ShouldBe(frame.Length);
        }
    }
}
