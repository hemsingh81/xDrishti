using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Depth;
using static XDrishti.Application.Tests.Depth.DepthTestData;

namespace XDrishti.Application.Tests.Depth;

/// <summary>P7-16 criterion 3: at most one snapshot per instrument per sample interval, only when a side changed and both were seen; flags; paise; the receive time and the monotonic clock.</summary>
public sealed class DepthSamplerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 10, 0, 0, TimeSpan.FromMinutes(330));

    private static DepthSampler Sampler(int ms = 1000) => new(Set(), TimeSpan.FromMilliseconds(ms));

    [Fact]
    public void Nothing_is_made_until_both_sides_were_seen()
    {
        var sampler = Sampler();

        sampler.Offer(Bid(), T0, 0).ShouldBeNull();
        sampler.Due(T0.AddSeconds(5), 5000).ShouldBeEmpty();
        sampler.Offer(Ask(), T0.AddMilliseconds(10), 10).ShouldNotBeNull();
    }

    [Fact]
    public void A_snapshot_has_the_whole_book_in_paise_the_receive_time_and_the_bid_sequence()
    {
        var sampler = Sampler();
        sampler.Offer(Bid(sequence: 41), T0, 100);

        var snapshot = sampler.Offer(Ask(sequence: 42), T0.AddMilliseconds(5), 105)!;

        snapshot.InstrumentId.ShouldBe(Reliance);
        snapshot.Ts.ShouldBe(T0.AddMilliseconds(5));
        snapshot.RxMonoMs.ShouldBe(105);
        snapshot.Seq.ShouldBe(41);
        snapshot.Flags.ShouldBe((short)0);
        snapshot.BidPx[0].ShouldBe(10000);
        snapshot.BidPx[19].ShouldBe(9905);
        snapshot.AskPx[0].ShouldBe(10005);
        snapshot.BidQty[0].ShouldBe(100);
        snapshot.AskOrd[0].ShouldBe(3);
        snapshot.BidPx.Length.ShouldBe(20);
        snapshot.AskQty.Length.ShouldBe(20);
    }

    [Fact]
    public void Levels_beyond_the_book_are_zero()
    {
        var sampler = Sampler();
        sampler.Offer(Bid(levels: 5), T0, 0);

        var snapshot = sampler.Offer(Ask(levels: 3), T0, 1)!;

        snapshot.BidPx[5..].ShouldAllBe(p => p == 0);
        snapshot.BidQty[5..].ShouldAllBe(p => p == 0);
        snapshot.AskOrd[3..].ShouldAllBe(p => p == 0);
        snapshot.Flags.ShouldBe((short)0); // empty levels are not an unordered ladder
    }

    [Fact]
    public void At_most_one_snapshot_per_interval_and_a_change_inside_it_is_made_when_it_runs_out()
    {
        var sampler = Sampler();
        sampler.Offer(Bid(), T0, 0);
        sampler.Offer(Ask(), T0, 1).ShouldNotBeNull();

        sampler.Offer(Bid(quantity: 200), T0.AddMilliseconds(300), 301).ShouldBeNull(); // changed, but inside the interval
        sampler.Offer(Ask(quantity: 250), T0.AddMilliseconds(400), 401).ShouldBeNull();
        sampler.Due(T0.AddMilliseconds(800), 800).ShouldBeEmpty();

        var due = sampler.Due(T0.AddMilliseconds(1001), 1001).ShouldHaveSingleItem();
        due.BidQty[0].ShouldBe(200);
        due.AskQty[0].ShouldBe(250);
        due.Ts.ShouldBe(T0.AddMilliseconds(400)); // the receive time of its newest packet
        due.RxMonoMs.ShouldBe(401);
        sampler.Due(T0.AddMilliseconds(3000), 3000).ShouldBeEmpty(); // nothing changed since
    }

    [Fact]
    public void An_unchanged_side_makes_no_snapshot()
    {
        var sampler = Sampler();
        sampler.Offer(Bid(), T0, 0);
        sampler.Offer(Ask(), T0, 1).ShouldNotBeNull();

        sampler.Offer(Bid(), T0.AddSeconds(2), 2000).ShouldBeNull();
        sampler.Offer(Ask(), T0.AddSeconds(3), 3000).ShouldBeNull();
        sampler.Due(T0.AddSeconds(4), 4000).ShouldBeEmpty();
    }

    [Fact]
    public void A_changed_sequence_alone_is_not_a_change()
    {
        var sampler = Sampler();
        sampler.Offer(Bid(sequence: 1), T0, 0);
        sampler.Offer(Ask(), T0, 1).ShouldNotBeNull();

        sampler.Offer(Bid(sequence: 2), T0.AddSeconds(2), 2000).ShouldBeNull();
    }

    [Fact]
    public void Instruments_are_sampled_apart_and_a_security_outside_the_set_is_counted_not_stored()
    {
        var sampler = Sampler();
        sampler.Offer(Bid(RelianceId), T0, 0);
        sampler.Offer(Bid(TcsId), T0, 0);

        sampler.Offer(Ask(RelianceId), T0, 1)!.InstrumentId.ShouldBe(Reliance);
        sampler.Offer(Ask(TcsId), T0, 2)!.InstrumentId.ShouldBe(Tcs);
        sampler.Offer(Bid(9999), T0, 3).ShouldBeNull();
        sampler.Foreign.ShouldBe(1);
    }

    [Fact]
    public void A_side_older_than_five_seconds_is_flagged_stale()
    {
        var sampler = Sampler();
        sampler.Offer(Bid(), T0, 0);

        var snapshot = sampler.Offer(Ask(), T0.AddSeconds(6), 6000)!;

        (snapshot.Flags & DepthFlags.StaleSide).ShouldBe(DepthFlags.StaleSide);
        var fresh = Sampler();
        fresh.Offer(Bid(), T0, 0);
        fresh.Offer(Ask(), T0.AddSeconds(5), 5000)!.Flags.ShouldBe((short)0); // exactly five seconds is not older
    }

    [Fact]
    public void A_best_bid_at_or_above_the_best_ask_is_flagged_crossed()
    {
        var sampler = Sampler();
        sampler.Offer(Bid(best: 100.05), T0, 0);

        sampler.Offer(Ask(best: 100.05), T0, 1)!.Flags.ShouldBe(DepthFlags.Crossed);
        var open = Sampler();
        open.Offer(Bid(best: 100.00), T0, 0);
        open.Offer(Ask(best: 100.05), T0, 1)!.Flags.ShouldBe((short)0);
    }

    [Fact]
    public void An_empty_side_is_not_crossed()
    {
        var sampler = Sampler();
        sampler.Offer(Bid(levels: 0), T0, 0);

        sampler.Offer(Ask(), T0, 1)!.Flags.ShouldBe((short)0);
    }

    [Fact]
    public void A_quantity_above_the_int_range_is_clamped_and_flagged()
    {
        var sampler = Sampler();
        sampler.Offer(Bid(quantity: uint.MaxValue - 100), T0, 0);

        var snapshot = sampler.Offer(Ask(), T0, 1)!;

        snapshot.BidQty[0].ShouldBe(int.MaxValue);
        (snapshot.Flags & DepthFlags.QuantityClamped).ShouldBe(DepthFlags.QuantityClamped);
    }

    [Fact]
    public void A_ladder_out_of_price_order_is_flagged()
    {
        var sampler = Sampler();
        var bad = Bid() with { Prices = [.. Bid().Prices.Reverse()] };
        sampler.Offer(bad, T0, 0);

        var snapshot = sampler.Offer(Ask(), T0, 1)!;

        (snapshot.Flags & DepthFlags.UnorderedLadder).ShouldBe(DepthFlags.UnorderedLadder);
    }

    [Theory]
    [InlineData(0.285, 29)] // half away from zero, not the binary 28.499...
    [InlineData(0.284, 28)]
    [InlineData(100.005, 10001)]
    [InlineData(2500.00, 250000)]
    [InlineData(0.0, 0)]
    public void Prices_become_paise_half_away_from_zero(double price, int paise)
    {
        var sampler = Sampler();
        var bid = Bid() with { Prices = [price, .. new double[19]] };
        sampler.Offer(bid, T0, 0);

        sampler.Offer(Ask(best: 5000), T0, 1)!.BidPx[0].ShouldBe(paise);
    }

    [Fact]
    public void The_monotonic_clock_shows_a_wall_clock_jump()
    {
        var sampler = Sampler();
        sampler.Offer(Bid(), T0, 1000);

        var snapshot = sampler.Offer(Ask(), T0.AddHours(-3), 1500)!; // the wall clock went back; the monotonic clock did not

        snapshot.Ts.ShouldBe(T0.AddHours(-3));
        snapshot.RxMonoMs.ShouldBe(1500);
    }

    [Fact]
    public void A_disconnect_packet_is_not_offered_to_a_book()
    {
        var sampler = Sampler();

        sampler.Offer(DepthPacket.Disconnect(805), T0, 0).ShouldBeNull();
    }

    /// <summary>The stored rows equal an independent reference sampler for random packet sequences (P7-16 test plan, property test).</summary>
    [Fact]
    public void For_random_packet_sequences_the_rows_equal_a_reference_sampler()
    {
        var random = new Random(716);
        for (var run = 0; run < 25; run++)
        {
            var sampler = Sampler();
            var reference = new Reference(TimeSpan.FromMilliseconds(1000));
            var mono = 0L;
            var actual = new List<(long Mono, double BidBest, double AskBest)>();
            for (var step = 0; step < 300; step++)
            {
                mono += random.Next(1, 700);
                var now = T0.AddMilliseconds(mono);
                var packet = random.Next(2) == 0
                    ? Bid(best: 100 + random.Next(0, 3) * 0.05, quantity: (uint)random.Next(1, 3))
                    : Ask(best: 100.5 + random.Next(0, 3) * 0.05, quantity: (uint)random.Next(1, 3));
                if (sampler.Offer(packet, now, mono) is { } snapshot)
                {
                    actual.Add((snapshot.RxMonoMs, snapshot.BidPx[0], snapshot.AskPx[0]));
                }

                reference.Offer(packet, mono);
            }

            actual.Select(a => a.Mono).ShouldBe(reference.Emitted.Select(e => e.Mono));
            actual.Select(a => (a.BidBest, a.AskBest)).ShouldBe(reference.Emitted.Select(e => (e.Bid, e.Ask)));
        }
    }

    /// <summary>The rule in its plainest form: a packet emits when both sides are known, something changed since the last emission and the interval has passed.</summary>
    private sealed class Reference(TimeSpan interval)
    {
        private DepthPacket? _bid;
        private DepthPacket? _ask;
        private bool _changed;
        private long? _last;

        public List<(long Mono, double Bid, double Ask)> Emitted { get; } = [];

        public void Offer(DepthPacket packet, long mono)
        {
            if (packet.Kind == DepthPacketKind.Bid)
            {
                _changed |= _bid is null || !_bid.Prices.SequenceEqual(packet.Prices) || !_bid.Quantities.SequenceEqual(packet.Quantities) || !_bid.Orders.SequenceEqual(packet.Orders);
                _bid = packet;
            }
            else
            {
                _changed |= _ask is null || !_ask.Prices.SequenceEqual(packet.Prices) || !_ask.Quantities.SequenceEqual(packet.Quantities) || !_ask.Orders.SequenceEqual(packet.Orders);
                _ask = packet;
            }

            if (_bid is not null && _ask is not null && _changed && (_last is null || mono - _last >= interval.TotalMilliseconds))
            {
                _changed = false;
                _last = mono;
                Emitted.Add((mono, Math.Round(_bid.Prices[0] * 100), Math.Round(_ask.Prices[0] * 100)));
            }
        }
    }
}
