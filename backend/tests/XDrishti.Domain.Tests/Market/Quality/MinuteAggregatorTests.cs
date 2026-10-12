using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;
using static XDrishti.Domain.Tests.Market.Quality.QualityFixtures;

namespace XDrishti.Domain.Tests.Market.Quality;

/// <summary>The quality check's own aggregator against buckets worked out by hand.</summary>
public sealed class MinuteAggregatorTests
{
    private static readonly int Open = Minute(9, 15);

    private static List<Bar> FourBars() =>
    [
        Of(Fri, Open, 100, 101, 99, 100.5, 10),
        Of(Fri, Open + 1, 100.5, 103, 100, 102, 20),
        Of(Fri, Open + 2, 102, 102.5, 98, 99, 30),
        Of(Fri, Open + 5, 99, 100, 97, 98, 40),
    ];

    [Fact]
    public void Five_minute_buckets_start_at_nine_fifteen_and_take_open_first_high_max_low_min_close_last_and_the_volume_sum()
    {
        var buckets = MinuteAggregator.Build(FourBars());

        buckets[(Timeframe.M5, Epoch(Fri, Open))].ShouldBe(new AggBucket(100, 103, 98, 99, 60, 3));
        buckets[(Timeframe.M5, Epoch(Fri, Open + 5))].ShouldBe(new AggBucket(99, 100, 97, 98, 40, 1));
    }

    [Fact]
    public void Wider_buckets_hold_every_bar_of_their_window()
    {
        var buckets = MinuteAggregator.Build(FourBars());

        buckets[(Timeframe.M10, Epoch(Fri, Open))].ShouldBe(new AggBucket(100, 103, 97, 98, 100, 4));
        buckets[(Timeframe.M15, Epoch(Fri, Open))].ShouldBe(new AggBucket(100, 103, 97, 98, 100, 4));
        buckets[(Timeframe.M30, Epoch(Fri, Open))].ShouldBe(new AggBucket(100, 103, 97, 98, 100, 4));
        buckets[(Timeframe.H1, Epoch(Fri, Open))].ShouldBe(new AggBucket(100, 103, 97, 98, 100, 4));
    }

    [Fact]
    public void The_day_the_week_and_the_month_start_at_india_midnight_of_the_day_the_monday_and_the_first()
    {
        var buckets = MinuteAggregator.Build(FourBars());
        var all = new AggBucket(100, 103, 97, 98, 100, 4);

        buckets[(Timeframe.D1, Epoch(Fri, 0))].ShouldBe(all);
        buckets[(Timeframe.W1, Epoch(new DateOnly(2026, 10, 5), 0))].ShouldBe(all);
        buckets[(Timeframe.Mo1, Epoch(new DateOnly(2026, 10, 1), 0))].ShouldBe(all);
        buckets.Count.ShouldBe(2 + 1 + 1 + 1 + 1 + 3); // 5m (2) + 10m + 15m + 30m + 1h, then 1D, 1W, 1M
    }

    [Fact]
    public void The_order_of_the_input_does_not_matter()
    {
        var shuffled = FourBars().OrderByDescending(b => b.Volume).ToList();

        MinuteAggregator.Build(shuffled).ShouldBe(MinuteAggregator.Build(FourBars()));
    }

    [Fact]
    public void A_week_across_a_month_end_is_one_week_bucket_and_two_month_buckets()
    {
        var wed = new DateOnly(2026, 9, 30);
        var thu = new DateOnly(2026, 10, 1);
        var nextMonday = new DateOnly(2026, 10, 5);
        var buckets = MinuteAggregator.Build([Flat(wed, Open, 50), Flat(thu, Open, 60), Flat(nextMonday, Open, 70)]);

        buckets[(Timeframe.W1, Epoch(new DateOnly(2026, 9, 28), 0))].ShouldBe(new AggBucket(50, 60, 50, 60, 20, 2));
        buckets[(Timeframe.W1, Epoch(nextMonday, 0))].ShouldBe(new AggBucket(70, 70, 70, 70, 10, 1));
        buckets[(Timeframe.Mo1, Epoch(new DateOnly(2026, 9, 1), 0))].ShouldBe(new AggBucket(50, 50, 50, 50, 10, 1));
        buckets[(Timeframe.Mo1, Epoch(new DateOnly(2026, 10, 1), 0))].ShouldBe(new AggBucket(60, 70, 60, 70, 20, 2));
    }

    [Fact]
    public void A_sunday_belongs_to_the_week_that_began_on_the_monday_before()
    {
        var muhurat = new DateOnly(2026, 11, 8);

        var buckets = MinuteAggregator.Build([Flat(muhurat, Minute(18, 15), 100)]);

        buckets.ContainsKey((Timeframe.W1, Epoch(new DateOnly(2026, 11, 2), 0))).ShouldBeTrue();
        buckets.ContainsKey((Timeframe.D1, Epoch(muhurat, 0))).ShouldBeTrue();
    }

    [Fact]
    public void An_evening_session_is_cut_into_buckets_counted_from_nine_fifteen_so_eighteen_fifteen_starts_one()
    {
        var muhurat = new DateOnly(2026, 11, 8);
        var buckets = MinuteAggregator.Build([Flat(muhurat, Minute(18, 15), 100), Flat(muhurat, Minute(18, 16), 101), Flat(muhurat, Minute(18, 20), 102)]);

        buckets[(Timeframe.M5, Epoch(muhurat, Minute(18, 15)))].Bars.ShouldBe(2);
        buckets[(Timeframe.M5, Epoch(muhurat, Minute(18, 20)))].Bars.ShouldBe(1);
        buckets[(Timeframe.H1, Epoch(muhurat, Minute(18, 15)))].Bars.ShouldBe(3);
    }

    [Fact]
    public void The_last_hour_and_the_last_ten_minutes_of_the_session_hold_only_what_the_session_has()
    {
        var bars = Enumerable.Range(Minute(15, 15), 15).Select(m => Flat(Fri, m, 100)).ToList();

        var buckets = MinuteAggregator.Build(bars);

        buckets[(Timeframe.H1, Epoch(Fri, Minute(15, 15)))].Bars.ShouldBe(15);
        buckets[(Timeframe.M30, Epoch(Fri, Minute(15, 15)))].Bars.ShouldBe(15);
        buckets[(Timeframe.M10, Epoch(Fri, Minute(15, 25)))].Bars.ShouldBe(5);
        buckets.ContainsKey((Timeframe.H1, Epoch(Fri, Minute(14, 15)))).ShouldBeFalse();
    }

    [Fact]
    public void A_minute_before_the_open_falls_into_the_bucket_before_it()
    {
        var buckets = MinuteAggregator.Build([Flat(Fri, Minute(9, 12), 100)]);

        buckets.ContainsKey((Timeframe.M5, Epoch(Fri, Minute(9, 10)))).ShouldBeTrue();
        buckets.ContainsKey((Timeframe.H1, Epoch(Fri, Minute(8, 15)))).ShouldBeTrue();
    }

    [Fact]
    public void No_bars_give_no_buckets()
    {
        MinuteAggregator.Build([]).ShouldBeEmpty();
    }

    [Fact]
    public void The_helpers_name_the_india_midnights_of_the_day_the_week_and_the_month()
    {
        MinuteAggregator.MidnightOf(Fri).ShouldBe(Epoch(Fri, 0));
        MinuteAggregator.WeekStartOf(Fri).ShouldBe(Epoch(Mon, 0));
        MinuteAggregator.WeekStartOf(Mon).ShouldBe(Epoch(Mon, 0));
        MinuteAggregator.MonthStartOf(Fri).ShouldBe(Epoch(new DateOnly(2026, 10, 1), 0));
    }
}
