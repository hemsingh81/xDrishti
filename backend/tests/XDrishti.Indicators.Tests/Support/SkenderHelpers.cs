using Skender.Stock.Indicators;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>Shared plumbing of the Skender.Stock.Indicators cross-checks: rounded series (the decimal quotes carry exactly the doubles we feed), quotes, running an indicator, comparing arrays.</summary>
internal static class SkenderHelpers
{
    public static readonly int[] Periods = [2, 5, 14, 20];

    /// <summary>A seeded random series with prices rounded to hundredths.</summary>
    public static List<Bar> Rounded(int seed) =>
        [.. SeriesGenerator.Random(seed, 1_000, 5_000, specialSessions: seed % 2 == 0).Select(b => b with { Open = Math.Round(b.Open, 2), High = Math.Round(b.High, 2), Low = Math.Round(b.Low, 2), Close = Math.Round(b.Close, 2) })];

    public static List<Quote> Quotes(List<Bar> bars) =>
        [.. bars.Select(b => new Quote { Date = DateTimeOffset.FromUnixTimeSeconds(b.EpochSeconds).UtcDateTime, Open = (decimal)b.Open, High = (decimal)b.High, Low = (decimal)b.Low, Close = (decimal)b.Close, Volume = b.Volume })];

    public static double[] Run(IIndicator indicator, List<Bar> bars)
    {
        var values = new double[bars.Count];
        for (var i = 0; i < bars.Count; i++)
        {
            indicator.Update(bars[i]);
            values[i] = indicator.Value;
        }

        return values;
    }

    public static double[] FromSkender(IEnumerable<double?> values) => [.. values.Select(v => v ?? double.NaN)];

    public static double[] FromSkender(IEnumerable<decimal?> values) => [.. values.Select(v => v is null ? double.NaN : (double)v.Value)];

    public static void ShouldAgree(double[] ours, double[] theirs, string label)
    {
        ours.Length.ShouldBe(theirs.Length);
        for (var i = 0; i < ours.Length; i++)
        {
            if (!Tolerance.Same(theirs[i], ours[i]))
            {
                Assert.Fail($"{label}, bar {i}: Skender {theirs[i]:R}, ours {ours[i]:R}");
            }
        }
    }
}
