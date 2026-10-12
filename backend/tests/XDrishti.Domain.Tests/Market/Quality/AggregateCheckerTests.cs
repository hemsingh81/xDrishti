using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;
using static XDrishti.Domain.Tests.Market.Quality.QualityFixtures;

namespace XDrishti.Domain.Tests.Market.Quality;

public sealed class AggregateCheckerTests
{
    private static readonly QualityBounds Bounds = QualityWindow.Bounds(Mon);

    private static IReadOnlyList<BucketComparison> Compare(IReadOnlyList<Bar> bars, IReadOnlyList<StoredBucket> stored) => AggregateChecker.Compare(bars, stored, Bounds);

    private static StoredBucket Find(List<StoredBucket> stored, Timeframe timeframe, long start) => stored.Single(b => b.Timeframe == timeframe && b.Start == start);

    private static List<StoredBucket> Replace(List<StoredBucket> stored, StoredBucket old, StoredBucket changed)
    {
        var copy = stored.ToList();
        copy[copy.IndexOf(old)] = changed;
        return copy;
    }

    [Fact]
    public void Aggregates_that_match_the_bars_exactly_pass_in_every_timeframe()
    {
        var bars = FullWindow();

        var (check, differences) = AggregateChecker.Summarize(Compare(bars, StoredFrom(bars)));

        (check.Status, check.Differences).ShouldBe((CheckStatus.Pass, 0));
        differences.ShouldBeEmpty();
        check.ByTimeframe.Keys.ShouldBe(["5m", "10m", "15m", "30m", "1h", "1D", "1W", "1M"]);
        check.ByTimeframe.Values.ShouldAllBe(t => t.Status == CheckStatus.Pass && t.Compared > 0);
        check.ByTimeframe["1D"].Compared.ShouldBe(5);
        check.ByTimeframe["5m"].Compared.ShouldBe(5 * 75);
        check.ByTimeframe["1W"].Compared.ShouldBe(1);
    }

    [Fact]
    public void A_tampered_high_fails_its_timeframe_only_and_names_the_bucket_and_the_field()
    {
        var bars = FullWindow();
        var stored = StoredFrom(bars);
        var bucket = Find(stored, Timeframe.M15, Epoch(Wed, Minute(10, 0)));

        var (check, differences) = AggregateChecker.Summarize(Compare(bars, Replace(stored, bucket, bucket with { High = bucket.High + 1 })));

        (check.Status, check.Differences).ShouldBe((CheckStatus.Fail, 1));
        check.ByTimeframe["15m"].ShouldBe(new TimeframeCheck(CheckStatus.Fail, 125, 1));
        check.ByTimeframe.Where(t => t.Key != "15m").ShouldAllBe(t => t.Value.Status == CheckStatus.Pass);
        var difference = differences.ShouldHaveSingleItem();
        difference.Timeframe.ShouldBe("15m");
        difference.Fields.ShouldBe(["high"]);
        difference.Ts.ShouldBe(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.FromMinutes(330)));
    }

    [Theory]
    [InlineData("open")]
    [InlineData("high")]
    [InlineData("low")]
    [InlineData("close")]
    [InlineData("volume")]
    [InlineData("bars")]
    public void Every_compared_value_is_compared_exactly(string field)
    {
        var bars = Session(Fri);
        var stored = StoredFrom(bars);
        var day = Find(stored, Timeframe.D1, Epoch(Fri, 0));
        var changed = field switch
        {
            "open" => day with { Open = day.Open + 0.000001 },
            "high" => day with { High = day.High + 0.000001 },
            "low" => day with { Low = day.Low - 0.000001 },
            "close" => day with { Close = day.Close + 0.000001 },
            "volume" => day with { Volume = day.Volume + 1 },
            _ => day with { Bars = day.Bars + 1 },
        };

        var rows = Compare(bars, Replace(stored, day, changed));

        rows.Single(r => r.Differs).Fields.ShouldBe([field]);
    }

    [Fact]
    public void A_bucket_the_bars_give_but_that_is_not_stored_is_missing()
    {
        var bars = FullWindow();
        var stored = StoredFrom(bars);
        stored.Remove(Find(stored, Timeframe.H1, Epoch(Thu, Minute(11, 15))));

        var (check, differences) = AggregateChecker.Summarize(Compare(bars, stored));

        check.ByTimeframe["1h"].Differences.ShouldBe(1);
        differences.ShouldHaveSingleItem().Fields.ShouldBe(["missing"]);
    }

    [Fact]
    public void A_bucket_that_is_stored_but_has_no_bars_is_unexpected()
    {
        var bars = FullWindow();
        var stored = StoredFrom(bars);
        stored.Add(new StoredBucket(Timeframe.M5, Epoch(Fri, Minute(16, 0)), 1, 1, 1, 1, 1, 1));

        var rows = Compare(bars, stored);

        rows.Single(r => r.Differs).Fields.ShouldBe(["unexpected"]);
    }

    [Fact]
    public void Aggregates_not_refreshed_after_late_data_differ_in_every_timeframe_the_late_minutes_touch()
    {
        var all = FullWindow();
        var stored = StoredFrom(all.Where(b => b.EpochSeconds != Epoch(Fri, Minute(15, 29))).ToList());

        var (check, _) = AggregateChecker.Summarize(Compare(all, stored));

        check.ByTimeframe.Values.ShouldAllBe(t => t.Differences == 1, "the last minute of the week sits in exactly one bucket of every timeframe");
        check.Differences.ShouldBe(8);
    }

    [Fact]
    public void Stored_buckets_before_the_window_are_left_alone_but_the_week_and_month_of_its_first_day_are_compared()
    {
        var before = new StoredBucket(Timeframe.D1, Epoch(new DateOnly(2026, 9, 25), 0), 1, 2, 1, 1, 5, 1);
        var bars = FullWindow();
        var stored = StoredFrom(bars);
        stored.Add(before);

        var rows = Compare(bars, stored);

        rows.ShouldNotContain(r => r.Start == before.Start);
        rows.ShouldContain(r => r.Timeframe == Timeframe.W1 && r.Start == Epoch(Mon, 0));
        rows.ShouldContain(r => r.Timeframe == Timeframe.Mo1 && r.Start == Epoch(new DateOnly(2026, 10, 1), 0));
    }

    [Fact]
    public void A_week_bucket_that_started_before_the_window_is_recomputed_from_the_bars_read_back_to_the_week_start()
    {
        // Window starts on Wednesday: the week bucket includes Monday and Tuesday, which the check reads as well.
        var bounds = QualityWindow.Bounds(Wed);
        var bars = Sessions(Mon, Tue, Wed);
        var stored = StoredFrom(bars);

        var rows = AggregateChecker.Compare(bars, stored, bounds);

        rows.ShouldAllBe(r => !r.Differs);
        rows.Single(r => r.Timeframe == Timeframe.W1).Recomputed!.Value.Bars.ShouldBe(3 * 375);
        rows.Count(r => r.Timeframe == Timeframe.D1).ShouldBe(1);
    }

    [Fact]
    public void The_rows_are_ordered_by_timeframe_and_time_and_carry_both_sides()
    {
        var bars = Session(Fri);

        var rows = Compare(bars, StoredFrom(bars));

        rows.Select(r => r.Timeframe).Distinct().ShouldBe(Timeframes.Aggregated);
        rows.Where(r => r.Timeframe == Timeframe.M5).Select(r => r.Start).ShouldBeInOrder();
        rows.ShouldAllBe(r => r.Stored != null && r.Recomputed != null);
    }

    [Fact]
    public void The_list_of_differences_is_capped_but_the_counts_are_exact()
    {
        var bars = FullWindow();

        var (check, differences) = AggregateChecker.Summarize(Compare(bars, [])); // nothing stored: every bucket is missing

        check.Differences.ShouldBe(5 * (75 + 38 + 25 + 13 + 7 + 1) + 1 + 1);
        differences.Count.ShouldBe(AggregateChecker.MaxDifferences);
    }
}
