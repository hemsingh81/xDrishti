using System.Text.RegularExpressions;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Live;
using XDrishti.Application.Platform.Heartbeats;
using static XDrishti.Application.Tests.Live.FeedTestData;

namespace XDrishti.Application.Tests.Live;

/// <summary>The per-response-code counters of the feed: what the heartbeat and the log show about the messages that were dropped (counts, codes and lengths only).</summary>
public sealed partial class FeedPacketStatsTests
{
    [Theory]
    [InlineData(FeedDropReason.TooShort, "tooShort")]
    [InlineData(FeedDropReason.UnknownCode, "unknownCode")]
    [InlineData(FeedDropReason.LengthMismatch, "lengthMismatch")]
    [InlineData(FeedDropReason.UnknownSegment, "unknownSegment")]
    [InlineData(FeedDropReason.BadPrice, "badPrice")]
    [InlineData(FeedDropReason.BadTime, "badTime")]
    [InlineData(FeedDropReason.NotBinary, "notBinary")]
    [InlineData(FeedDropReason.Oversized, "oversized")]
    public void Each_reason_increments_its_own_cell_and_nothing_else(FeedDropReason reason, string name)
    {
        var stats = new FeedPacketStats();

        stats.Record(2, 16, 16, reason);

        var cell = stats.Summary(8)["2"];
        cell.Decoded.ShouldBe(0);
        cell.Dropped.ShouldBe(1);
        cell.Reasons.ShouldBe(new Dictionary<string, long> { [name] = 1 });
    }

    [Fact]
    public void A_decoded_message_counts_as_decoded_and_not_as_dropped()
    {
        var stats = new FeedPacketStats();

        stats.Record(4, 50, 50, FeedDropReason.None);
        stats.Record(4, 50, 50, FeedDropReason.None);

        var cell = stats.Summary(8)["4"];
        (cell.Decoded, cell.Dropped).ShouldBe((2, 0));
        cell.Reasons.ShouldBeEmpty();
        stats.TotalDropped.ShouldBe(0);
    }

    [Fact]
    public void An_unknown_code_99_of_length_20_is_recorded_with_its_lengths()
    {
        var stats = new FeedPacketStats();

        stats.Record(99, 20, 18, FeedDropReason.UnknownCode);
        stats.Record(99, 22, 22, FeedDropReason.UnknownCode);

        var cell = stats.Summary(8)["99"];
        (cell.Dropped, cell.FirstLength, cell.LastLength, cell.FirstDeclaredLength).ShouldBe((2, 20, 22, 18));
        cell.Reasons["unknownCode"].ShouldBe(2);
    }

    [Fact]
    public void The_first_length_and_the_declared_length_of_the_first_message_do_not_change()
    {
        var stats = new FeedPacketStats();
        stats.Record(2, 16, 8, FeedDropReason.LengthMismatch); // a header that declares the payload only
        stats.Record(2, 16, 16, FeedDropReason.None);

        var cell = stats.Summary(8)["2"];

        (cell.FirstLength, cell.FirstDeclaredLength, cell.LastLength).ShouldBe((16, 8, 16));
    }

    [Fact]
    public void Packets_that_came_in_a_frame_with_several_are_counted_and_the_json_leaves_the_count_out_when_there_are_none()
    {
        var stats = new FeedPacketStats();
        stats.Record(4, 116, 50, FeedDropReason.None, inMultiPacketFrame: true);
        stats.Record(4, 116, 50, FeedDropReason.None, inMultiPacketFrame: true);
        stats.Record(2, 16, 16, FeedDropReason.None);

        var summary = stats.Summary(8);

        summary["4"].InMultiPacketFrames.ShouldBe(2);
        summary["2"].InMultiPacketFrames.ShouldBe(0);
        var json = FeedJson.Write(new FeedHeartbeat(true, 1, null, 0, 0, 0, 0, summary));
        json.ShouldContain("\"inMultiPacketFrames\":2");
        json.Split("inMultiPacketFrames").Length.ShouldBe(2); // only code 4 has the field
    }

    [Fact]
    public void A_message_with_no_code_is_counted_under_none_and_a_message_too_short_for_a_length_has_no_declared_length()
    {
        var stats = new FeedPacketStats();

        stats.Record(null, 5, null, FeedDropReason.NotBinary);
        stats.Record(7, 2, null, FeedDropReason.TooShort);

        var summary = stats.Summary(8);
        summary["none"].Reasons["notBinary"].ShouldBe(1);
        summary["7"].FirstDeclaredLength.ShouldBeNull();
    }

    [Fact]
    public void A_decoded_message_of_an_instrument_outside_the_set_moves_from_decoded_to_dropped()
    {
        var stats = new FeedPacketStats();
        stats.Record(2, 16, 16, FeedDropReason.None);
        stats.Record(2, 16, 16, FeedDropReason.None);

        stats.Reclassify(2, FeedDropReason.UnknownInstrument);

        var cell = stats.Summary(8)["2"];
        (cell.Decoded, cell.Dropped).ShouldBe((1, 1));
        cell.Reasons["unknownInstrument"].ShouldBe(1);
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var stats = new FeedPacketStats();
        stats.Record(99, 20, 20, FeedDropReason.UnknownCode);

        stats.Reset();

        stats.Summary(8).ShouldBeEmpty();
        stats.TotalDropped.ShouldBe(0);
        stats.TopDropped(5).ShouldBeEmpty();
    }

    [Fact]
    public void The_cardinality_is_bounded_whatever_the_feed_sends()
    {
        var stats = new FeedPacketStats();
        var random = new Random(3);
        var reasons = Enum.GetValues<FeedDropReason>();
        for (var i = 0; i < 20_000; i++)
        {
            stats.Record(random.Next(-5, 400), random.Next(0, 5000), random.Next(0, 70_000), reasons[random.Next(reasons.Length)]);
        }

        stats.Summary(int.MaxValue).Count.ShouldBeLessThanOrEqualTo(257); // 256 codes and "none"
        stats.Summary(8).Count.ShouldBe(8);
        stats.TopDropped(5).Count.ShouldBe(5);
    }

    [Fact]
    public void The_summary_lists_the_busiest_codes_first()
    {
        var stats = new FeedPacketStats();
        for (var i = 0; i < 3; i++)
        {
            stats.Record(2, 16, 16, FeedDropReason.None);
        }

        for (var i = 0; i < 10; i++)
        {
            stats.Record(99, 20, 20, FeedDropReason.UnknownCode);
        }

        stats.Summary(1).Keys.ShouldBe(["99"]);
        stats.Summary(8).Keys.ShouldBe(["99", "2"]);
    }

    [Fact]
    public void The_top_dropped_groups_are_by_code_and_reason_biggest_first()
    {
        var stats = new FeedPacketStats();
        for (var i = 0; i < 5; i++)
        {
            stats.Record(99, 20, 20, FeedDropReason.UnknownCode);
        }

        for (var i = 0; i < 2; i++)
        {
            stats.Record(2, 14, 16, FeedDropReason.LengthMismatch);
        }

        stats.Record(2, 16, 16, FeedDropReason.BadPrice);

        stats.TopDropped(2).ShouldBe([new DroppedPacketGroup("99", FeedDropReason.UnknownCode, 5, 20), new DroppedPacketGroup("2", FeedDropReason.LengthMismatch, 2, 16)]);
    }

    // ---- the heartbeat JSON ----

    private static FeedPacketStats Busy()
    {
        var stats = new FeedPacketStats();
        for (var code = 0; code < 40; code++)
        {
            foreach (var reason in new[] { FeedDropReason.UnknownCode, FeedDropReason.LengthMismatch, FeedDropReason.BadPrice, FeedDropReason.BadTime })
            {
                stats.Record(code, 16 + code, 16, reason);
            }

            stats.Record(code, 16, 16, FeedDropReason.None);
        }

        return stats;
    }

    [Fact]
    public void The_heartbeat_carries_packets_by_code_and_reads_back_the_same()
    {
        var stats = new FeedPacketStats();
        stats.Record(2, 16, 16, FeedDropReason.None);
        stats.Record(99, 20, 18, FeedDropReason.UnknownCode);
        var details = new FeedHeartbeat(true, 3, null, 1.5, 0, 1, 4, stats.Summary(8));

        var json = FeedJson.Write(details);
        var back = FeedJson.Read(json)!;

        json.ShouldContain("\"packetsByCode\":{");
        json.ShouldContain("\"99\":{\"decoded\":0,\"dropped\":1,\"reasons\":{\"unknownCode\":1},\"firstLength\":20,\"lastLength\":20,\"firstDeclaredLength\":18}");
        back.PacketsByCode!["99"].ShouldBe(details.PacketsByCode!["99"], new PacketCodeSummaryComparer());
        back.PacketsByCode["2"].Decoded.ShouldBe(1);
        back.PacketsByCode["99"].Reasons["unknownCode"].ShouldBe(1);
    }

    [Fact]
    public void A_heartbeat_without_packets_has_no_such_field_and_an_old_row_reads_without_it()
    {
        var json = FeedJson.Write(new FeedHeartbeat(true, 3, null, 1.5, 0, 1, 4));

        json.ShouldNotContain("packetsByCode");
        FeedJson.Read("{\"connected\":true,\"subscribed\":1,\"lastTickAt\":null,\"ticksPerSecond\":0,\"reconnectsToday\":0,\"droppedPackets\":0,\"flushMs\":1}")!.PacketsByCode.ShouldBeNull();
    }

    [Fact]
    public void Even_with_every_code_busy_the_heartbeat_is_under_two_kilobytes()
    {
        var metrics = new FeedMetrics(Busy());

        var json = FeedJson.Write(metrics.Snapshot(new DateTimeOffset(2026, 10, 12, 5, 0, 0, TimeSpan.Zero)));

        System.Text.Encoding.UTF8.GetByteCount(json).ShouldBeLessThan(2048);
        FeedJson.Read(json)!.PacketsByCode!.Count.ShouldBe(FeedMetrics.MaxCodesInHeartbeat);
    }

    [Fact]
    public void The_metrics_snapshot_leaves_the_field_out_until_something_was_received()
    {
        var metrics = new FeedMetrics();

        metrics.Snapshot(DateTimeOffset.UnixEpoch).PacketsByCode.ShouldBeNull();
        metrics.Packets.Record(2, 16, 16, FeedDropReason.None);
        metrics.Snapshot(DateTimeOffset.UnixEpoch).PacketsByCode.ShouldNotBeNull();
    }

    // ---- the manager: reclassify, reset, the log line ----

    [Fact]
    public async Task A_decoded_tick_of_an_unknown_instrument_is_dropped_with_that_reason_and_a_new_window_starts_clean()
    {
        using var world = new FeedWorld();
        AddSet(world);
        world.Metrics.Packets.Record(99, 20, 20, FeedDropReason.UnknownCode); // left over from an earlier window
        await world.StartConnectedAsync();
        world.Metrics.Packets.Summary(8).ShouldBeEmpty(); // reset at the start of the window
        world.Metrics.Packets.Record(2, 16, 16, FeedDropReason.None); // what the connection records when it decodes the packet

        world.Connector.Connection(0)!.Push(Tick(FeedSegments.NseEquity, 77777, 10m, world.Clock.GetUtcNow()));
        await FeedWorld.UntilAsync(() => world.Metrics.Dropped == 1);

        var cell = world.Metrics.Packets.Summary(8)["2"];
        (cell.Decoded, cell.Dropped).ShouldBe((0, 1));
        cell.Reasons["unknownInstrument"].ShouldBe(1);
        await world.StopAsync();
    }

    [Fact]
    public async Task Once_a_minute_the_top_dropped_groups_are_logged_without_any_packet_bytes()
    {
        using var world = new FeedWorld();
        AddSet(world);
        await world.StartConnectedAsync();
        for (var i = 0; i < 1370; i++)
        {
            world.Metrics.Packets.Record(99, 20, 18, FeedDropReason.UnknownCode);
        }

        world.Metrics.Packets.Record(2, 14, 16, FeedDropReason.LengthMismatch);

        await world.RunClockUntilAsync(() => world.Log.Events.Any(e => e.Event == 4125), TimeSpan.FromSeconds(10));

        var line = world.Log.Events.First(e => e.Event == 4125).Message;
        line.ShouldContain("code=99 reason=unknownCode count=1370 lastLength=20");
        line.ShouldContain("code=2 reason=lengthMismatch count=1 lastLength=14");
        SummaryLine().IsMatch(line).ShouldBeTrue(line);
        await world.StopAsync();
    }

    [Fact]
    public async Task Nothing_is_logged_when_nothing_was_dropped()
    {
        using var world = new FeedWorld();
        AddSet(world);
        await world.StartConnectedAsync();
        world.Metrics.Packets.Record(2, 16, 16, FeedDropReason.None);
        var start = world.Flush.Flushes;

        await world.RunClockUntilAsync(() => world.Flush.Flushes >= start + 40, TimeSpan.FromSeconds(2));

        world.Log.Events.ShouldNotContain(e => e.Event == 4125);
        await world.StopAsync();
    }

    private static void AddSet(FeedWorld world) =>
        world.Provider.Candidates.Add(Holding(Reliance, "2885", 5000m)); // no index: a minute without a packet is not silence here

    [GeneratedRegex(@"^feed\.packet_summary dropped=\d+ top=\[(code=(\d+|none) reason=[A-Za-z]+ count=\d+ lastLength=\d+(; )?)+\]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex SummaryLine();

    private sealed class PacketCodeSummaryComparer : IEqualityComparer<PacketCodeSummary>
    {
        public bool Equals(PacketCodeSummary? x, PacketCodeSummary? y) =>
            x is not null && y is not null
            && (x.Decoded, x.Dropped, x.FirstLength, x.LastLength, x.FirstDeclaredLength) == (y.Decoded, y.Dropped, y.FirstLength, y.LastLength, y.FirstDeclaredLength)
            && x.Reasons.OrderBy(r => r.Key).SequenceEqual(y.Reasons.OrderBy(r => r.Key));

        public int GetHashCode(PacketCodeSummary obj) => obj.Decoded.GetHashCode();
    }
}
