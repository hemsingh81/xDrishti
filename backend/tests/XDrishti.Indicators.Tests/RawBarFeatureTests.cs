using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>The raw bar features open, high, low, close and volume (criterion 3 of P3-01b): the value of the last closed bar of the timeframe.</summary>
public sealed class RawBarFeatureTests
{
    private static readonly string[] Names = ["open", "high", "low", "close", "volume"];

    private static readonly Timeframe[] Timeframes = [Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.D1];

    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 12)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_raw_feature_equals_that_value_of_the_last_closed_bar_of_its_timeframe_at_every_minute(int seed)
    {
        var bars = SeriesGenerator.Random(seed, 600, 2_500, specialSessions: true);
        var specs = Timeframes.SelectMany(t => Names.Select(n => new FeatureSpec(n, t))).ToList();
        var frame = FeatureFrame.Create(specs).Value;
        var closedByStep = Timeframes.ToDictionary(t => t, t => FrameReference.ClosedByStep(bars, t));
        var last = Timeframes.ToDictionary(t => t, _ => (ReferenceBucket?)null);

        for (var t = 0; t < bars.Count; t++)
        {
            frame.Step(bars[t]);
            foreach (var timeframe in Timeframes)
            {
                if (closedByStep[timeframe][t].Count > 0)
                {
                    last[timeframe] = closedByStep[timeframe][t][^1];
                }
            }

            for (var h = 0; h < specs.Count; h++)
            {
                var want = last[specs[h].Timeframe] is { } bucket ? ReferencePatterns.Raw([bucket.AsBar], specs[h].Name)[0] : double.NaN;
                if (!Tolerance.Identical(want, frame[h]) || frame.IsReady(h) == double.IsNaN(want))
                {
                    Assert.Fail($"seed {seed}, {TestSpecs.Describe(specs[h])}, step {t}: expected {want:R}, frame has {frame[h]:R}");
                }
            }
        }
    }

    [Fact]
    public void The_daily_close_is_not_ready_before_the_first_daily_close_and_stays_yesterdays_until_today_closes()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.D1), new FeatureSpec("close", Timeframe.M1), new FeatureSpec("volume", Timeframe.D1)]).Value;
        var monday = BarFixtures.Walk(BarFixtures.Mon, Minutes.Open, 375);
        var tuesday = BarFixtures.Walk(BarFixtures.Tue, Minutes.Open, 375);

        for (var i = 0; i < monday.Count - 1; i++)
        {
            frame.Step(monday[i]);
            frame.IsReady(0).ShouldBeFalse($"minute {i}");
            frame[0].ShouldBeNaN();
        }

        frame.Step(monday[^1]);
        frame.IsReady(0).ShouldBeTrue();
        frame[0].ShouldBe(monday[^1].Close);
        frame[2].ShouldBe(monday.Sum(b => (double)b.Volume));

        for (var i = 0; i < tuesday.Count - 1; i++)
        {
            frame.Step(tuesday[i]);
            frame[0].ShouldBe(monday[^1].Close, $"yesterday's close until today's daily bar closes (minute {i})");
            frame[1].ShouldBe(tuesday[i].Close);
        }

        frame.Step(tuesday[^1]);
        frame[0].ShouldBe(tuesday[^1].Close);
        frame.At(0, 1).ShouldBe(monday[^1].Close);
    }

    [Fact]
    public void The_fifteen_minute_close_is_the_close_of_the_last_closed_bucket_at_every_minute_of_a_day()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.M15)]).Value;
        var day = BarFixtures.Get("normal day");

        for (var i = 0; i < day.Count; i++)
        {
            frame.Step(day[i]);
            var closedBuckets = (i + 1) / 15;
            if (closedBuckets == 0)
            {
                frame.IsReady(0).ShouldBeFalse();
            }
            else
            {
                frame[0].ShouldBe(day[closedBuckets * 15 - 1].Close, $"minute {i}");
            }
        }
    }

    [Fact]
    public void The_volume_of_a_bar_is_the_sum_of_its_minutes_including_zero_volume_ones()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("volume", Timeframe.M5)]).Value;
        var bars = new List<Bar>
        {
            Minutes.Bar(BarFixtures.Mon, Minutes.Open, 1, 1, 1, 1, 100),
            Minutes.Bar(BarFixtures.Mon, Minutes.Open + 1, 1, 1, 1, 1, 0),
            Minutes.Bar(BarFixtures.Mon, Minutes.Open + 2, 1, 1, 1, 1, 50),
            Minutes.Bar(BarFixtures.Mon, Minutes.Open + 3, 1, 1, 1, 1, 0),
            Minutes.Bar(BarFixtures.Mon, Minutes.Open + 4, 1, 1, 1, 1, 7),
        };

        foreach (var bar in bars)
        {
            frame.Step(bar);
        }

        frame[0].ShouldBe(157.0);
    }

    [Fact]
    public void A_raw_feature_ignores_the_source_and_shares_its_instance()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.M5), new FeatureSpec("close", Timeframe.M5, PriceSource.High), new FeatureSpec("open", Timeframe.M5)]).Value;

        frame.InstanceCount.ShouldBe(2);
        frame.HandleOf(new FeatureSpec("close", Timeframe.M5, PriceSource.Low)).ShouldBe(0);
    }

    [Fact]
    public void Volume_is_a_feature_not_a_price_source()
    {
        var sma = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M1, PriceSource.Volume, 3)]);
        var raw = FeatureFrame.Create([new FeatureSpec("volume", Timeframe.M1, PriceSource.Volume)]);

        sma.IsFailure.ShouldBeTrue();
        sma.Error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("specs[0].source");
        raw.IsFailure.ShouldBeTrue();
        FeatureFrame.Create([new FeatureSpec("volume", Timeframe.M1)]).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_raw_bar_indicator_itself_reads_each_value_and_rejects_the_default_source()
    {
        var bar = Minutes.Bar(BarFixtures.Mon, Minutes.Open, 10, 20, 5, 15, 99);

        foreach (var (source, value) in new[] { (PriceSource.Open, 10.0), (PriceSource.High, 20.0), (PriceSource.Low, 5.0), (PriceSource.Close, 15.0), (PriceSource.Volume, 99.0) })
        {
            var raw = new RawBar(source);
            raw.IsReady.ShouldBeFalse();
            raw.Update(bar);
            raw.Value.ShouldBe(value);
            raw.WarmUp.ShouldBe(1);
            raw.Reset();
            raw.IsReady.ShouldBeFalse();
        }

        Should.Throw<ArgumentOutOfRangeException>(() => new RawBar(PriceSource.Default));
    }
}
