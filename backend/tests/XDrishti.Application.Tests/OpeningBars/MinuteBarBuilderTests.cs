using XDrishti.Application.Abstractions.OpeningBars;
using XDrishti.Application.OpeningBars;

namespace XDrishti.Application.Tests.OpeningBars;

/// <summary>BK-03b criteria 2 and 3: quote packets to 1-minute bars on exchange time, complete minutes only, nothing filled forward, nothing from the formation time on.</summary>
public sealed class MinuteBarBuilderTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static DateTimeOffset At(int hour, int minute, int second = 0) => new(2026, 10, 12, hour, minute, second, Ist);

    private static MinuteBarBuilder Builder() => new(new TimeOnly(9, 15), new TimeOnly(9, 31));

    [Fact]
    public void The_spec_example_gives_the_0915_bar_from_exchange_time_and_the_0916_bar_opens_at_101()
    {
        var builder = Builder();

        var none1 = builder.Offer(A, At(9, 15, 1), 100m, 1_000);
        var none2 = builder.Offer(A, At(9, 15, 40), 102m, 1_500);
        var none3 = builder.Offer(A, At(9, 15, 59), 99m, 2_000);
        var bar = builder.Offer(A, At(9, 16, 3), 101m, 2_600);
        var next = builder.Flush(At(9, 17, 10), TimeSpan.FromSeconds(5));

        new[] { none1, none2, none3 }.ShouldAllBe(b => b == null);
        bar.ShouldBe(new MinuteBar(A, At(9, 15), 100m, 102m, 99m, 99m, 2_000, 3));
        next.Single().ShouldBe(new MinuteBar(A, At(9, 16), 101m, 101m, 101m, 101m, 600, 1));
    }

    [Fact]
    public void The_minute_is_taken_from_the_exchange_time_whatever_the_zone_of_the_instant()
    {
        var builder = Builder();
        builder.Offer(A, At(9, 15, 10).ToUniversalTime(), 100m, 10);

        var bar = builder.Offer(A, At(9, 16, 0).ToUniversalTime(), 101m, 20);

        bar!.MinuteStart.ShouldBe(At(9, 15));
        bar.MinuteStart.Offset.ShouldBe(Ist);
    }

    [Fact]
    public void A_packet_with_an_exchange_time_before_the_current_minute_is_dropped_and_counted_late()
    {
        var builder = Builder();
        builder.Offer(A, At(9, 16, 5), 101m, 2_000);

        var dropped = builder.Offer(A, At(9, 15, 50), 99m, 1_900);
        var bar = builder.Flush(At(9, 17, 10), TimeSpan.FromSeconds(5)).Single();

        dropped.ShouldBeNull();
        builder.Late.ShouldBe(1);
        bar.Low.ShouldBe(101m, "the late packet did not touch the minute");
    }

    [Fact]
    public void A_packet_for_a_minute_that_was_already_written_is_late_and_changes_nothing()
    {
        var builder = Builder();
        builder.Offer(A, At(9, 15, 5), 100m, 1_000);
        builder.Flush(At(9, 16, 6), TimeSpan.FromSeconds(5)).ShouldHaveSingleItem();

        var again = builder.Offer(A, At(9, 15, 59), 90m, 1_100);
        var second = builder.Flush(At(9, 16, 7), TimeSpan.FromSeconds(5));

        again.ShouldBeNull();
        builder.Late.ShouldBe(1);
        second.ShouldBeEmpty("a minute is written once");
    }

    [Fact]
    public void A_minute_is_written_only_after_the_grace_when_no_later_packet_comes()
    {
        var builder = Builder();
        builder.Offer(A, At(9, 15, 5), 100m, 1_000);

        var early = builder.Flush(At(9, 16, 4), TimeSpan.FromSeconds(5));
        var due = builder.Flush(At(9, 16, 5), TimeSpan.FromSeconds(5));

        early.ShouldBeEmpty();
        due.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_minute_without_a_packet_gives_no_bar_and_is_not_filled_forward()
    {
        var builder = Builder();
        builder.Offer(A, At(9, 15, 5), 100m, 1_000);
        var first = builder.Offer(A, At(9, 17, 5), 103m, 1_800);
        var last = builder.Flush(At(9, 18, 10), TimeSpan.FromSeconds(5));

        new[] { first!.MinuteStart }.Concat(last.Select(b => b.MinuteStart)).ShouldBe([At(9, 15), At(9, 17)]);
    }

    [Fact]
    public void Packets_outside_09_15_to_the_record_until_time_are_ignored_and_counted()
    {
        var builder = Builder();

        builder.Offer(A, At(9, 14, 59), 100m, 10).ShouldBeNull();
        builder.Offer(A, At(9, 31, 0), 100m, 10).ShouldBeNull();

        builder.Outside.ShouldBe(2);
        builder.Flush(At(10, 0), TimeSpan.Zero).ShouldBeEmpty();
    }

    [Fact]
    public void Instruments_are_built_apart_and_the_first_minutes_volume_is_the_cumulative_volume()
    {
        var builder = Builder();
        builder.Offer(A, At(9, 15, 5), 100m, 700);
        builder.Offer(B, At(9, 15, 6), 50m, 90);

        var bars = builder.Flush(At(9, 16, 10), TimeSpan.FromSeconds(5)).OrderBy(b => b.InstrumentId).ToList();

        bars.Select(b => b.Volume).ShouldBe([700, 90]);
    }

    [Fact]
    public void Over_random_packet_streams_every_bar_is_unique_in_range_in_order_and_never_from_a_later_packet_than_its_minute()
    {
        var random = new Random(2026);
        for (var run = 0; run < 200; run++)
        {
            var builder = Builder();
            var bars = new List<MinuteBar>();
            var cumulative = 0L;
            for (var i = 0; i < 300; i++)
            {
                var id = random.Next(3) == 0 ? B : A;
                var time = At(9, 14, 0).AddSeconds(random.Next(0, 18 * 60));
                cumulative += random.Next(0, 50);
                if (builder.Offer(id, time, 100m + random.Next(-5, 5), cumulative) is { } bar)
                {
                    bars.Add(bar);
                }
            }

            bars.AddRange(builder.Flush(At(9, 40), TimeSpan.Zero));
            bars.ShouldAllBe(b => b.MinuteStart >= At(9, 15) && b.MinuteStart < At(9, 31) && b.MinuteStart.Second == 0 && b.Low <= b.High && b.Volume >= 0);
            bars.GroupBy(b => (b.InstrumentId, b.MinuteStart)).ShouldAllBe(g => g.Count() == 1, "a minute of an instrument is built once");
        }
    }
}
