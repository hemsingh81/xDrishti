using System.Buffers.Binary;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>P7-16 criterion 2: the parser decodes bid and ask packets of 332 bytes (little endian), walks stacked packets, and drops-and-counts - never throws - a wrong length, an unknown code, another segment, a bad price and a truncated tail. Frames are constructed (UNVERIFIED).</summary>
public sealed class DhanDepth20ParserTests
{
    [Fact]
    public void A_bid_packet_of_332_bytes_is_decoded_little_endian()
    {
        var bytes = DepthPackets.Bid(2885, 2950.25, sequence: 4_000_000_000);

        var used = DhanDepth20Parser.ParseNext(bytes, out var packet, out var reason);

        used.ShouldBe(332);
        reason.ShouldBe(DepthDropReason.None);
        packet.ShouldNotBeNull();
        packet.Kind.ShouldBe(DepthPacketKind.Bid);
        packet.SecurityId.ShouldBe(2885);
        packet.Sequence.ShouldBe(4_000_000_000u);
        packet.Prices[0].ShouldBe(2950.25);
        packet.Prices[19].ShouldBe(2950.25 - (0.05 * 19), 1e-9);
        packet.Quantities[0].ShouldBe(100u);
        packet.Quantities[19].ShouldBe(119u);
        packet.Orders[1].ShouldBe(3u);
        packet.Prices.Length.ShouldBe(20);
    }

    [Theory]
    [InlineData("depth20-bid.hex", DepthPacketKind.Bid, 1650.00, 90001u)]
    [InlineData("depth20-ask.hex", DepthPacketKind.Ask, 1650.05, 90002u)]
    public void The_constructed_fixtures_of_the_samples_folder_decode(string file, DepthPacketKind kind, double best, uint sequence)
    {
        var bytes = FeedPackets.Fixture(file);

        DhanDepth20Parser.ParseNext(bytes, out var packet, out var reason).ShouldBe(332);

        reason.ShouldBe(DepthDropReason.None);
        packet!.Kind.ShouldBe(kind);
        packet.SecurityId.ShouldBe(1333);
        packet.Sequence.ShouldBe(sequence);
        packet.Prices[0].ShouldBe(best);
        packet.Quantities[1].ShouldBe(110u);
        packet.Orders[2].ShouldBe(5u);
    }

    [Fact]
    public void An_ask_packet_is_decoded_as_the_ask_side()
    {
        DhanDepth20Parser.ParseNext(DepthPackets.Ask(1333), out var packet, out _);

        packet!.Kind.ShouldBe(DepthPacketKind.Ask);
    }

    [Fact]
    public void The_byte_order_is_little_endian()
    {
        var bytes = DepthPackets.Bid(0x01020304);
        // the same security id written big endian is a different number: the parser reads little endian
        var wrong = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32BigEndian(wrong.AsSpan(4), 0x01020304);

        DhanDepth20Parser.ParseNext(bytes, out var right, out _);
        DhanDepth20Parser.ParseNext(wrong, out var other, out _);

        right!.SecurityId.ShouldBe(0x01020304);
        other?.SecurityId.ShouldNotBe(0x01020304);
    }

    [Fact]
    public void A_frame_of_several_packets_is_walked_one_by_one()
    {
        var frame = DepthPackets.Frame(DepthPackets.Bid(1), DepthPackets.Ask(1), DepthPackets.Bid(2));
        var seen = new List<(DepthPacketKind Kind, int Id)>();

        var offset = 0;
        while (offset < frame.Length)
        {
            offset += DhanDepth20Parser.ParseNext(frame.AsSpan(offset), out var packet, out _);
            seen.Add((packet!.Kind, packet.SecurityId));
        }

        seen.ShouldBe([(DepthPacketKind.Bid, 1), (DepthPacketKind.Ask, 1), (DepthPacketKind.Bid, 2)]);
        offset.ShouldBe(996);
    }

    [Fact]
    public void An_unusable_packet_in_a_frame_is_dropped_and_the_walk_goes_on_after_it()
    {
        var bad = DepthPackets.Bid(1);
        DepthPackets.SetPrice(bad, 3, double.NaN);
        var frame = DepthPackets.Frame(bad, DepthPackets.Ask(1));

        var first = DhanDepth20Parser.ParseNext(frame, out var dropped, out var reason);
        var second = DhanDepth20Parser.ParseNext(frame.AsSpan(first), out var kept, out _);

        dropped.ShouldBeNull();
        reason.ShouldBe(DepthDropReason.BadPrice);
        first.ShouldBe(332);
        kept!.Kind.ShouldBe(DepthPacketKind.Ask);
        (first + second).ShouldBe(664);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.05)]
    [InlineData(21_000_001d)]
    public void A_price_that_is_not_finite_or_is_negative_or_too_large_drops_the_packet(double price)
    {
        var bytes = DepthPackets.Bid(1);
        DepthPackets.SetPrice(bytes, 7, price);

        DhanDepth20Parser.ParseNext(bytes, out var packet, out var reason);

        packet.ShouldBeNull();
        reason.ShouldBe(DepthDropReason.BadPrice);
    }

    [Fact]
    public void A_zero_price_is_an_empty_level_not_a_bad_price()
    {
        var bytes = DepthPackets.Book(41, 1, 1, 100, -0.05, filled: 5);

        DhanDepth20Parser.ParseNext(bytes, out var packet, out _);

        packet!.Prices[5..].ShouldAllBe(p => p == 0d);
    }

    [Fact]
    public void An_unknown_code_is_dropped_whole_and_walked_past()
    {
        var bytes = DepthPackets.Book(77, 1, 1, 100, 0.05);

        var used = DhanDepth20Parser.ParseNext(bytes, out var packet, out var reason);

        packet.ShouldBeNull();
        reason.ShouldBe(DepthDropReason.UnknownCode);
        used.ShouldBe(332);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void A_segment_other_than_nse_equity_is_dropped(byte segment)
    {
        DhanDepth20Parser.ParseNext(DepthPackets.Book(41, 1, 1, 100, -0.05, segment: segment), out var packet, out var reason);

        packet.ShouldBeNull();
        reason.ShouldBe(DepthDropReason.UnknownSegment);
    }

    [Fact]
    public void A_security_id_that_is_not_positive_is_dropped()
    {
        DhanDepth20Parser.ParseNext(DepthPackets.Bid(0), out var packet, out var reason);

        packet.ShouldBeNull();
        reason.ShouldBe(DepthDropReason.UnknownSegment);
    }

    [Theory]
    [InlineData(300)] // shorter than a book
    [InlineData(340)] // longer than a book
    public void A_declared_length_that_is_not_332_for_a_book_is_dropped(int declared)
    {
        var bytes = DepthPackets.Book(41, 1, 1, 100, -0.05, length: declared);

        DhanDepth20Parser.ParseNext(bytes, out var packet, out var reason);

        packet.ShouldBeNull();
        reason.ShouldBe(DepthDropReason.LengthMismatch);
    }

    [Fact]
    public void A_tail_shorter_than_the_declared_length_is_used_up_and_counted_once()
    {
        var bytes = DepthPackets.Bid(1)[..200];

        var used = DhanDepth20Parser.ParseNext(bytes, out var packet, out var reason);

        used.ShouldBe(200);
        packet.ShouldBeNull();
        reason.ShouldBe(DepthDropReason.LengthMismatch);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void A_remainder_shorter_than_a_length_field_is_too_short(int length)
    {
        var used = DhanDepth20Parser.ParseNext(new byte[length], out var packet, out var reason);

        used.ShouldBe(length);
        packet.ShouldBeNull();
        reason.ShouldBe(DepthDropReason.TooShort);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)1)]
    [InlineData((short)3)]
    [InlineData((short)-5)]
    public void A_length_below_four_cannot_be_walked_on(short declared)
    {
        var bytes = new byte[40];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, declared);

        var used = DhanDepth20Parser.ParseNext(bytes, out var packet, out var reason);

        used.ShouldBe(40);
        packet.ShouldBeNull();
        reason.ShouldBe(DepthDropReason.LengthMismatch);
    }

    [Fact]
    public void A_disconnect_packet_carries_its_reason_code()
    {
        var used = DhanDepth20Parser.ParseNext(DepthPackets.Disconnect(805), out var packet, out _);

        used.ShouldBe(14);
        packet!.Kind.ShouldBe(DepthPacketKind.Disconnect);
        packet.Reason.ShouldBe(805);
    }

    [Fact]
    public void A_short_disconnect_packet_reads_its_reason_after_an_eight_byte_header()
    {
        var bytes = new byte[10];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, 10);
        bytes[2] = 50;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(8), 806);

        DhanDepth20Parser.ParseNext(bytes, out var packet, out _);

        packet!.Reason.ShouldBe(806);
    }

    [Fact]
    public void Random_and_hostile_bytes_never_throw_and_always_move_forward()
    {
        var random = new Random(7016);
        for (var i = 0; i < 2000; i++)
        {
            var bytes = new byte[random.Next(0, 800)];
            random.NextBytes(bytes);
            if (bytes.Length > 3 && i % 2 == 0)
            {
                BinaryPrimitives.WriteInt16LittleEndian(bytes, (short)random.Next(-5, 900)); // a believable length field
            }

            var offset = 0;
            while (offset < bytes.Length)
            {
                var used = DhanDepth20Parser.ParseNext(bytes.AsSpan(offset), out _, out _);
                used.ShouldBeGreaterThan(0);
                offset += used;
            }
        }
    }

    [Fact]
    public void A_truncated_valid_packet_at_every_length_is_dropped_without_throwing()
    {
        var bytes = DepthPackets.Bid(1);
        for (var cut = 0; cut < bytes.Length; cut++)
        {
            DhanDepth20Parser.ParseNext(bytes.AsSpan(0, cut), out var packet, out _);

            packet.ShouldBeNull();
        }
    }
}
