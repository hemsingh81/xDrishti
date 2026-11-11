using XDrishti.Domain.Assistant;

namespace XDrishti.Domain.Tests.Assistant;

/// <summary>Spec BK-12 AC 7: a series is cut to at most 120 points for the model, keeping the first, the last and the extremes; deterministic.</summary>
public sealed class SeriesSamplerTests
{
    private static double?[] Wave(int n) => [.. Enumerable.Range(0, n).Select(i => (double?)Math.Sin(i / 7.0) * 10 + i % 5)];

    [Fact]
    public void A_short_series_is_kept_whole_and_not_marked_sampled()
    {
        var sample = SeriesSampler.Sample(50, 120, Wave(50));

        sample.Sampled.ShouldBeFalse();
        sample.Step.ShouldBe(1);
        sample.Indices.Count.ShouldBe(50);
    }

    [Theory]
    [InlineData(121)]
    [InlineData(300)]
    [InlineData(2500)]
    public void A_long_series_is_cut_to_at_most_120_points_with_first_last_and_extremes(int n)
    {
        var a = Wave(n);
        var b = a.Select(v => v * -1.5).ToArray();

        var sample = SeriesSampler.Sample(n, 120, a, b);

        sample.Sampled.ShouldBeTrue();
        sample.Indices.Count.ShouldBeLessThanOrEqualTo(120);
        sample.Indices[0].ShouldBe(0);
        sample.Indices[^1].ShouldBe(n - 1);
        sample.Indices.ShouldBe([.. sample.Indices.Order()]);
        foreach (var s in new[] { a, b })
        {
            sample.Indices.ShouldContain(Array.IndexOf(s, s.Min()));
            sample.Indices.ShouldContain(Array.IndexOf(s, s.Max()));
        }
    }

    [Fact]
    public void The_cut_is_deterministic_and_skips_missing_values_when_looking_for_extremes()
    {
        double?[] withGaps = [.. Enumerable.Range(0, 400).Select(i => i % 3 == 0 ? null : (double?)(i % 50))];

        var one = SeriesSampler.Sample(400, 120, withGaps);
        var two = SeriesSampler.Sample(400, 120, withGaps);

        one.Indices.ShouldBe(two.Indices);
        one.Indices.Count.ShouldBeLessThanOrEqualTo(120);
    }
}
