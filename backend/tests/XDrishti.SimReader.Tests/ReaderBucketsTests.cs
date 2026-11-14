namespace XDrishti.SimReader.Tests;

/// <summary>The closing rule of evaluation bars (shared by the readers): a decision merges buckets closing at one minute, a series of bars keeps each; the 15:15 hourly stub closes at the 15:29 step.</summary>
public sealed class ReaderBucketsTests
{
    private static List<ReaderBar> Minutes(params string[] at)
    {
        var day = new DatedDay(new DateOnly(2026, 3, 2));
        foreach (var m in at)
        {
            day.Flat(m, 100m);
        }

        return [.. day.Hole("09:16", "09:34").Hole("09:36", "09:58").Hole("10:00", "15:29").Bars()];
    }

    [Fact]
    public void Two_fifteen_minute_buckets_closing_at_one_minute_are_one_decision_and_two_bars()
    {
        // bars: 09:15, 09:35 (bucket 1, span 09:30 to 09:44) and 09:59 (bucket 2): bucket 1 closes at the first minute from 09:44 on that is present, 09:59; bucket 2 closes there too
        var bars = Minutes("09:15", "09:35", "09:59");
        var buckets = ReaderBuckets.Build(bars, 15);

        var decision = ReaderBuckets.Closes(bars, buckets, 15);
        var series = ReaderBuckets.Closes(bars, buckets, 15, false);

        decision.Select(c => c.Bucket).ShouldBe([0, 2]);
        series.Select(c => c.Bucket).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void The_last_partial_hourly_bucket_closes_at_the_15_29_step()
    {
        var day = new DatedDay(new DateOnly(2026, 3, 2)).Flat("09:15", 100m).Bars();
        var bars = ReaderClock.SessionBars(day);
        var hourly = ReaderBuckets.Build(bars, 60);

        var closes = ReaderBuckets.Closes(bars, hourly, 60, false);

        hourly.Count.ShouldBe(7);
        closes.Count.ShouldBe(7);
        closes[^1].Closing.ShouldBe(bars.Count - 1);
    }
}
