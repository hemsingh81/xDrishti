using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>The aggregate phase against a real TimescaleDB, bar for bar against an independent reference implementation (ADR 0016 §6, design 05 §4).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataAggregateTests(ApiFactory factory) : DataTestBase(factory)
{
    /// <summary>
    /// Wed 25 Feb to Tue 3 Mar 2026 across a month and a week boundary: a Thursday with a gap of a few minutes, a session that ends at 14:00, a Muhurat-style
    /// 60-bar evening session on the Sunday, and a Saturday special session of 30 bars in the morning.
    /// </summary>
    private static List<Bar> Fixture(int seed)
    {
        var bars = new List<Bar>();
        bars.AddRange(BarFixtures.Session(new DateOnly(2026, 2, 25), seed));
        bars.AddRange(BarFixtures.Session(new DateOnly(2026, 2, 26), seed, skip: m => m is >= 10 * 60 and < 10 * 60 + 4));
        bars.AddRange(BarFixtures.Session(new DateOnly(2026, 2, 27), seed, count: 285));
        bars.AddRange(BarFixtures.Session(new DateOnly(2026, 2, 28), seed, count: 30));
        bars.AddRange(BarFixtures.EveningSession(new DateOnly(2026, 3, 1), seed));
        bars.AddRange(BarFixtures.Session(new DateOnly(2026, 3, 2), seed));
        bars.AddRange(BarFixtures.Session(new DateOnly(2026, 3, 3), seed, skip: m => m == 15 * 60 + 20));
        return bars;
    }

    [Fact]
    public async Task Every_timeframe_matches_the_reference_bar_for_bar_including_the_evening_session_and_the_partial_last_hour()
    {
        var abb = await SeedAsync("ABB");
        var tcs = await SeedAsync("TCS");
        var abbBars = Fixture(1);
        var tcsBars = Fixture(2);
        WriteCsv("ABB", abbBars);
        WriteCsv("TCS", tcsBars);

        var job = await RunAsync();

        job.Aggregate.Status.ShouldBe("succeeded", job.Aggregate.Message);
        (job.Aggregate.Done, job.Aggregate.Total, job.Aggregate.Full).ShouldBe((2, 2, false));
        foreach (var (id, bars) in new[] { (abb, abbBars), (tcs, tcsBars) })
        {
            var expected = ReferenceAggregator.Build(bars);
            var actual = await StoredAggregatesAsync(id);
            actual.Keys.OrderBy(k => k).ShouldBe(expected.Keys.OrderBy(k => k));
            foreach (var key in expected.Keys)
            {
                actual[key].ShouldBe(expected[key], $"{key.Timeframe} at {DateTimeOffset.FromUnixTimeSeconds(key.Start)}");
            }
        }

        job.Aggregate.Buckets.ShouldBe(await ScalarAsync<long>("SELECT count(*) FROM market.bars_agg"));
        var perTimeframe = job.Aggregate.BucketsByTimeframe;
        perTimeframe.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(["10m", "15m", "1D", "1M", "1W", "1h", "30m", "5m"]);
        perTimeframe["1D"].ShouldBe(14); // 7 days with bars for each of the two instruments
        perTimeframe["1M"].ShouldBe(4);  // February and March
        perTimeframe["1W"].ShouldBe(4);  // the weeks of 23 Feb and of 2 Mar
    }

    [Fact]
    public async Task The_reference_agrees_with_the_rules_on_the_special_cases()
    {
        var abb = await SeedAsync("ABB");
        WriteCsv("ABB", Fixture(1));

        await RunAsync();
        var aggregates = await StoredAggregatesAsync(abb);
        long At(DateOnly date, int minute) => BarFixtures.Epoch(date, minute);
        var friday = new DateOnly(2026, 2, 27);
        var thursday = new DateOnly(2026, 2, 26);
        var sunday = new DateOnly(2026, 3, 1);

        // 1h: 09:15 … 14:15, then the partial 15:15–15:30 with its 15 bars; a session that ends at 14:00 (Friday: 285 bars) has no bucket after 13:15, which holds 45 bars.
        var partial = aggregates[("1h", At(new DateOnly(2026, 2, 25), 15 * 60 + 15))];
        (partial.Bars, partial.Complete).ShouldBe((15, true));
        aggregates.ContainsKey(("1h", At(friday, 14 * 60 + 15))).ShouldBeFalse();
        var last = aggregates[("1h", At(friday, 13 * 60 + 15))];
        (last.Bars, last.Complete).ShouldBe((45, false));

        // The gap on Thursday 10:00–10:03 makes the 10:00 5m bucket, the 10m, 15m, 30m and 1h buckets around it and the day incomplete.
        aggregates[("5m", At(thursday, 10 * 60))].Complete.ShouldBeFalse();
        aggregates[("5m", At(thursday, 10 * 60 + 5))].Complete.ShouldBeTrue();
        aggregates[("1h", At(thursday, 9 * 60 + 15))].Complete.ShouldBeFalse();
        aggregates[("1D", At(thursday, 0))].Complete.ShouldBeFalse();
        aggregates[("1D", At(thursday, 0))].Bars.ShouldBe(371);

        // The evening session (60 bars from 18:15): complete in every timeframe, the day too, although it has no regular-session bar.
        aggregates[("1h", At(sunday, 18 * 60 + 15))].ShouldBe(aggregates[("1h", At(sunday, 18 * 60 + 15))] with { Bars = 60, Complete = true });
        aggregates[("15m", At(sunday, 18 * 60 + 15))].Complete.ShouldBeTrue();
        aggregates[("1D", At(sunday, 0))].ShouldBe(aggregates[("1D", At(sunday, 0))] with { Bars = 60, Complete = true });

        // Weeks start on Monday: the Sunday evening belongs to the week of Monday 23 Feb, which also holds the incomplete days.
        aggregates.ContainsKey(("1W", At(new DateOnly(2026, 2, 23), 0))).ShouldBeTrue();
        aggregates[("1W", At(new DateOnly(2026, 2, 23), 0))].Complete.ShouldBeFalse();
        aggregates[("1W", At(new DateOnly(2026, 2, 23), 0))].Bars.ShouldBe(375 + 371 + 285 + 30 + 60);
        aggregates[("1M", At(new DateOnly(2026, 2, 1), 0))].Bars.ShouldBe(375 + 371 + 285 + 30);
        aggregates[("1M", At(new DateOnly(2026, 3, 1), 0))].Bars.ShouldBe(60 + 375 + 374);
    }

    [Fact]
    public async Task Building_again_writes_nothing_and_changes_nothing()
    {
        var abb = await SeedAsync("ABB");
        WriteCsv("ABB", Fixture(1));
        await RunAsync();
        var before = await StoredAggregatesAsync(abb);

        var rebuilt = await RunAsync(["aggregate"]);

        rebuilt.RebuildAll.ShouldBeTrue();
        (rebuilt.Aggregate.Full, rebuilt.Aggregate.Done, rebuilt.Aggregate.Total, rebuilt.Aggregate.Buckets).ShouldBe((true, 1, 1, 0));
        (await StoredAggregatesAsync(abb)).ShouldBe(before);
    }

    [Fact]
    public async Task A_later_load_rebuilds_only_the_range_it_touched_and_the_result_still_matches_the_reference()
    {
        var abb = await SeedAsync("ABB");
        var bars = Fixture(1);
        WriteCsv("ABB", bars);
        await RunAsync();

        // Thursday 5 March is new; the file is longer now.
        var more = BarFixtures.Session(new DateOnly(2026, 3, 5), 1);
        bars.AddRange(more);
        WriteCsv("ABB", bars, modified: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var job = await RunAsync();

        job.Csv.Inserted.ShouldBe(375);
        var reference = ReferenceAggregator.Build(bars);
        var actual = await StoredAggregatesAsync(abb);
        actual.Keys.OrderBy(k => k).ShouldBe(reference.Keys.OrderBy(k => k));
        foreach (var key in reference.Keys)
        {
            actual[key].ShouldBe(reference[key]);
        }

        // Only that day (75 five-minute, 38 ten-, 25 fifteen-, 13 thirty-minute and 7 hourly buckets, one day) and its week and month were written:
        // the new week (1) and the month of March, whose bar grew (1).
        job.Aggregate.BucketsByTimeframe.ShouldBe(
            new Dictionary<string, long> { ["5m"] = 75, ["10m"] = 38, ["15m"] = 25, ["30m"] = 13, ["1h"] = 7, ["1D"] = 1, ["1W"] = 1, ["1M"] = 1 }, ignoreOrder: true);
    }

    [Fact]
    public async Task A_dhan_bar_that_replaces_a_csv_bar_changes_the_buckets_that_contain_it()
    {
        var abb = await SeedAsync("ABB");
        var bars = BarFixtures.Session(LastSession, 1);
        WriteCsv("ABB", bars);
        await RunAsync();
        var changed = bars[100] with { High = bars[100].High + 50, Volume = bars[100].Volume + 1000 };
        await StoreAsync(abb, changed, "dhan");
        await ExecuteDirtyAsync(abb, changed);

        var job = await RunAsync(["aggregate"]);

        job.Aggregate.Full.ShouldBeTrue();
        bars[100] = changed;
        var reference = ReferenceAggregator.Build(bars);
        var actual = await StoredAggregatesAsync(abb);
        foreach (var key in reference.Keys)
        {
            actual[key].ShouldBe(reference[key]);
        }

        job.Aggregate.Buckets.ShouldBe(8); // one bucket of each timeframe contains that minute
    }

    /// <summary>Marks the range as changed the way a merge does, for a bar a test wrote straight into the table.</summary>
    private async Task ExecuteDirtyAsync(Guid instrumentId, Bar bar)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "UPDATE market.bar_coverage SET dirty_from = to_timestamp(@ts), dirty_to = to_timestamp(@ts) WHERE instrument_id = @id",
            ("ts", (double)bar.EpochSeconds), ("id", instrumentId));
    }
}
