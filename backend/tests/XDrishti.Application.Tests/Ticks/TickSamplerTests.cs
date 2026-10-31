using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Ticks;
using static XDrishti.Application.Tests.Ticks.TickTestData;

namespace XDrishti.Application.Tests.Ticks;

/// <summary>The change-only rule of the tick sampler (P7-16b criteria 3 and 4): rows only when the cumulative volume rose, the flags, paise, the stored part of the day, and a property check against an independent reference.</summary>
public sealed class TickSamplerTests
{
    private static long _seq;

    private static TradeTick? Offer(TickSampler sampler, FeedPacket packet, DateTimeOffset? at = null) => sampler.Offer(packet, at ?? T0, 5, ++_seq);

    [Fact]
    public void The_first_usable_packet_is_written_as_a_baseline_with_the_fields_of_the_row()
    {
        var sampler = new TickSampler(Set());

        var row = Offer(sampler, Quote(1_000, 2950.50m, ltq: 25, ltt: T0.AddSeconds(-1)))!;

        row.InstrumentId.ShouldBe(Reliance);
        row.Flags.ShouldBe(TickFlags.Baseline);
        row.CumVol.ShouldBe(1_000);
        row.LtpPx.ShouldBe(295_050);
        row.Ltq.ShouldBe(25);
        row.LttEpoch.ShouldBe((int)T0.AddSeconds(-1).ToUnixTimeSeconds());
        row.Ts.ShouldBe(T0);
        row.RxMonoMs.ShouldBe(5);
    }

    [Fact]
    public void A_row_is_written_only_when_the_cumulative_volume_rose_and_an_unchanged_one_is_counted()
    {
        var sampler = new TickSampler(Set());
        Offer(sampler, Quote(1_000));

        var same = Offer(sampler, Quote(1_000, price: 101m));
        var up = Offer(sampler, Quote(1_040));

        same.ShouldBeNull();
        sampler.Unchanged.ShouldBe(1);
        up.ShouldNotBeNull().Flags.ShouldBe((short)0);
        up.CumVol.ShouldBe(1_040);
    }

    [Fact]
    public void After_a_rebase_the_next_row_is_a_baseline_even_with_an_unchanged_volume()
    {
        var sampler = new TickSampler(Set());
        Offer(sampler, Quote(1_000));

        sampler.Rebase();
        var row = Offer(sampler, Quote(1_000));

        row.ShouldNotBeNull().Flags.ShouldBe(TickFlags.Baseline);
    }

    [Fact]
    public void A_volume_below_the_last_stored_is_written_with_volume_down()
    {
        var sampler = new TickSampler(Set());
        Offer(sampler, Quote(1_000));

        Offer(sampler, Quote(900)).ShouldNotBeNull().Flags.ShouldBe(TickFlags.VolumeDown);
    }

    [Fact]
    public void A_trade_time_earlier_than_the_last_stored_sets_ltt_back()
    {
        var sampler = new TickSampler(Set());
        Offer(sampler, Quote(1_000, ltt: T0));

        Offer(sampler, Quote(1_100, ltt: T0.AddSeconds(-3))).ShouldNotBeNull().Flags.ShouldBe(TickFlags.LttBack);
    }

    [Fact]
    public void A_last_quantity_of_65535_sets_ltq_capped()
    {
        var sampler = new TickSampler(Set());

        Offer(sampler, Quote(70_000, ltq: 65_535)).ShouldNotBeNull().Flags.ShouldBe((short)(TickFlags.Baseline | TickFlags.LtqCapped));
    }

    [Fact]
    public void A_missing_last_quantity_is_stored_as_zero()
    {
        var sampler = new TickSampler(Set());

        Offer(sampler, Quote(1_000, ltq: null)).ShouldNotBeNull().Ltq.ShouldBe(0);
    }

    [Theory]
    [InlineData("100.005", 10_001)]
    [InlineData("100.015", 10_002)]
    [InlineData("0.285", 29)]
    [InlineData("2950.5", 295_050)]
    public void Prices_become_paise_half_away_from_zero(string price, int paise)
    {
        TickSampler.TryPaise(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), out var value).ShouldBeTrue();

        value.ShouldBe(paise);
    }

    [Fact]
    public void A_price_too_large_for_the_paise_column_is_not_usable()
    {
        var sampler = new TickSampler(Set());

        Offer(sampler, Quote(1_000, price: 30_000_000m)).ShouldBeNull();
        sampler.Unusable.ShouldBe(1);
    }

    [Fact]
    public void Ticker_previous_close_disconnect_index_and_foreign_packets_are_counted_and_ignored()
    {
        var sampler = new TickSampler(Set());
        var security = new FeedSecurity(FeedSegments.NseEquity, RelianceId);

        Offer(sampler, new FeedPacket(FeedPacketKind.Ticker, security, 100m, T0)).ShouldBeNull();
        Offer(sampler, new FeedPacket(FeedPacketKind.PreviousClose, security, 100m)).ShouldBeNull();
        Offer(sampler, new FeedPacket(FeedPacketKind.Disconnect, default, Reason: 807)).ShouldBeNull();
        Offer(sampler, new FeedPacket(FeedPacketKind.Quote, new FeedSecurity(FeedSegments.Index, RelianceId), 100m, T0, 10)).ShouldBeNull();
        Offer(sampler, Quote(1_000, securityId: 999)).ShouldBeNull();

        sampler.Ignored.ShouldBe(5);
    }

    [Fact]
    public void A_quote_without_a_volume_is_not_usable()
    {
        var sampler = new TickSampler(Set());

        Offer(sampler, Quote(0) with { Volume = null }).ShouldBeNull();
        sampler.Unusable.ShouldBe(1);
    }

    [Theory]
    [InlineData(9, 14, 59, 900, false)]
    [InlineData(9, 15, 0, 0, true)]
    [InlineData(15, 29, 59, 999, true)]
    [InlineData(15, 30, 0, 0, false)]
    public void Only_receive_times_from_09_15_to_before_15_30_are_written(int hour, int minute, int second, int ms, bool written)
    {
        var sampler = new TickSampler(Set());
        var at = new DateTimeOffset(2026, 10, 12, hour, minute, second, ms, Ist);

        (Offer(sampler, Quote(1_000), at) is not null).ShouldBe(written);
    }

    [Fact]
    public void A_packet_outside_the_window_leaves_the_state_alone_so_the_first_one_inside_is_the_baseline()
    {
        var sampler = new TickSampler(Set());

        Offer(sampler, Quote(1_000), new DateTimeOffset(2026, 10, 12, 9, 14, 59, 900, Ist));
        var first = Offer(sampler, Quote(1_000), new DateTimeOffset(2026, 10, 12, 9, 15, 0, Ist));

        first.ShouldNotBeNull().Flags.ShouldBe(TickFlags.Baseline);
    }

    [Fact]
    public void Instruments_are_independent()
    {
        var sampler = new TickSampler(Set());
        Offer(sampler, Quote(1_000));

        var other = Offer(sampler, Quote(1_000, securityId: TcsId));

        other.ShouldNotBeNull().InstrumentId.ShouldBe(Tcs);
        other.Flags.ShouldBe(TickFlags.Baseline);
    }

    [Fact]
    public void Property_the_stored_rows_equal_an_independent_reference_on_random_packet_sequences()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var sampler = new TickSampler(Set());
            var reference = new Dictionary<int, (long Volume, int Ltt)>();
            var pending = new HashSet<int>();
            for (var i = 0; i < 300; i++)
            {
                if (random.Next(40) == 0)
                {
                    sampler.Rebase();
                    pending.UnionWith(reference.Keys);
                    continue;
                }

                var id = random.Next(3) switch { 0 => RelianceId, 1 => TcsId, _ => 77 };
                var volume = (long)random.Next(1, 30);
                var ltt = T0.AddSeconds(random.Next(-5, 5));
                var ltq = random.Next(6) == 0 ? 65_535 : random.Next(0, 100);
                var row = sampler.Offer(Quote(volume, ltq: ltq, ltt: ltt, securityId: id), T0, i, i);

                short? expected = null;
                if (id != 77)
                {
                    var epoch = (int)ltt.ToUnixTimeSeconds();
                    var known = reference.TryGetValue(id, out var last);
                    var baseline = !known || pending.Contains(id);
                    if (baseline || last.Volume != volume)
                    {
                        var flags = baseline ? 1 : volume < last.Volume ? 4 : 0;
                        flags |= known && epoch < last.Ltt ? 8 : 0;
                        flags |= ltq == 65_535 ? 2 : 0;
                        expected = (short)flags;
                        reference[id] = (volume, epoch);
                        pending.Remove(id);
                    }
                }

                (row?.Flags).ShouldBe(expected, $"seed {seed}, packet {i}");
                if (row is not null)
                {
                    row.CumVol.ShouldBe(volume);
                    row.RxSeq.ShouldBe(i);
                }
            }
        }
    }
}
