using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>Every indicator against an independent batch reference over seeded random series (gaps, zero volume, flat bars, gap-ups across sessions, special sessions).</summary>
public sealed class ReferenceComparisonTests
{
    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 24)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_indicator_equals_its_batch_reference_within_one_billionth(int seed)
    {
        var bars = SeriesGenerator.Random(seed, specialSessions: seed % 2 == 0);

        foreach (var spec in TestSpecs.Variants(Timeframe.M1))
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
            var indicator = info.Create(spec);
            var expected = ReferenceIndicators.Compute(spec, bars);

            for (var i = 0; i < bars.Count; i++)
            {
                indicator.Update(bars[i]);
                if (!Tolerance.Same(expected[i], indicator.Value))
                {
                    Assert.Fail($"seed {seed}, {TestSpecs.Describe(spec)}, bar {i}: expected {expected[i]:R}, was {indicator.Value:R}");
                }

                if (indicator.IsReady == double.IsNaN(expected[i]))
                {
                    Assert.Fail($"seed {seed}, {TestSpecs.Describe(spec)}, bar {i}: IsReady is {indicator.IsReady} but the reference is {expected[i]:R}");
                }
            }
        }
    }

    [Fact]
    public void The_generated_series_really_contain_the_awkward_cases()
    {
        var all = Enumerable.Range(1, 24).SelectMany(seed => SeriesGenerator.Random(seed, specialSessions: true)).ToList();

        all.Count(b => b.Volume == 0).ShouldBeGreaterThan(100);
        all.Count(b => b.Open == b.High && b.High == b.Low && b.Low == b.Close).ShouldBeGreaterThan(100);
        foreach (var seed in Enumerable.Range(1, 24))
        {
            SeriesGenerator.Random(seed).Count.ShouldBeInRange(1_000, 5_400);
        }
    }

    [Fact]
    public void The_same_seed_gives_the_same_series()
    {
        SeriesGenerator.Random(7).ShouldBe(SeriesGenerator.Random(7));
        SeriesGenerator.Random(7).ShouldNotBe(SeriesGenerator.Random(8));
    }
}
