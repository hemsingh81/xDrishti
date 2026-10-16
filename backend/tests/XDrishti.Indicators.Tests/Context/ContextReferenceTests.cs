using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Context;

/// <summary>
/// Criterion 8: every derived value of <see cref="MarketContext"/> equals an independent plain-loop reference (<see cref="ReferenceContext"/>) on seeded random daily series within 1e-9, with the stated warm-up.
/// The series have weekends and holidays as gaps, a VIX that is missing, late or absent, breadth that is sometimes absent, and phases that run through every trend and volatility level.
/// </summary>
public sealed class ContextReferenceTests
{
    /// <summary>Small numbers, so that the short series reach every state: the average and the slope after 8 days, the percentiles after 20.</summary>
    private static readonly ContextOptions Small = new(TrendSmaPeriod: 8, TrendSlopeDays: 3, AdxPeriod: 5, AdxMin: 20, AtrPeriod: 5, PercentileWindow: 20, LowVolBelow: 100.0 / 3, HighVolFrom: 200.0 / 3);

    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 30)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_value_equals_the_reference_with_the_default_options(int seed) => Compare(seed, new ContextOptions());

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_value_equals_the_reference_with_small_periods(int seed) => Compare(seed, Small);

    [Fact]
    public void Every_value_equals_the_reference_with_other_thresholds_and_a_one_day_slope()
    {
        var options = new ContextOptions(TrendSmaPeriod: 10, TrendSlopeDays: 1, AdxPeriod: 3, AdxMin: 35, AtrPeriod: 7, PercentileWindow: 15, LowVolBelow: 25, HighVolFrom: 55);

        foreach (var seed in Enumerable.Range(100, 12))
        {
            Compare(seed, options);
        }
    }

    [Fact]
    public void The_series_reach_every_value_and_all_nine_regimes_so_that_the_comparison_is_not_vacuous()
    {
        var regimes = new HashSet<double>();
        var seen = ContextRead.FieldNames.ToDictionary(n => n, _ => 0);
        foreach (var seed in Enumerable.Range(1, 30))
        {
            var days = ContextSeriesGenerator.Random(seed);
            var reference = ReferenceContext.Compute(days, Small);
            foreach (var value in reference.Regime.Where(v => !double.IsNaN(v)))
            {
                regimes.Add(value);
            }

            for (var f = 0; f < ContextRead.FieldNames.Length; f++)
            {
                seen[ContextRead.FieldNames[f]] += ContextRead.Columns(reference)[f].Count(v => !double.IsNaN(v));
            }
        }

        regimes.OrderBy(r => r).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8, 9]);
        seen.ShouldAllBe(pair => pair.Value > 100, "every field has plenty of ready values: " + string.Join(", ", seen.Select(p => p.Key + "=" + p.Value)));
    }

    [Fact]
    public void The_warm_up_with_the_default_options_is_55_days_for_the_trend_and_265_for_the_volatility_percentile()
    {
        var days = FullDays(400);
        var context = MarketContext.Create(days).Value;

        var values = ContextRead.Days(context, days);

        FirstReady(values, v => v.IndexAdx).ShouldBe(28);            // ADX(14) is ready at bar 2 * 14
        FirstReady(values, v => v.IndexTrend).ShouldBe(55);          // 50-day average + 5 days of slope
        FirstReady(values, v => v.RegimeTrend).ShouldBe(55);
        FirstReady(values, v => v.IndexVolPct).ShouldBe(265);        // ATR(14) from day 14, then a full window of 252 values
        FirstReady(values, v => v.RegimeVol).ShouldBe(265);
        FirstReady(values, v => v.Regime).ShouldBe(265);
        FirstReady(values, v => v.Vix).ShouldBe(1);
        FirstReady(values, v => v.VixChg).ShouldBe(2);
        FirstReady(values, v => v.VixPct).ShouldBe(252);
        FirstReady(values, v => v.Breadth).ShouldBe(1);
    }

    [Fact]
    public void The_warm_up_follows_the_options_the_slow_one_wins_for_the_trend()
    {
        // average 4 + slope 2 = day 6, ADX(5) = day 10: the trend waits for the ADX; window 6 over ATR(3) values: day 8
        var days = FullDays(60);
        var context = MarketContext.Create(days, new ContextOptions(TrendSmaPeriod: 4, TrendSlopeDays: 2, AdxPeriod: 5, AtrPeriod: 3, PercentileWindow: 6)).Value;

        var values = ContextRead.Days(context, days);

        FirstReady(values, v => v.IndexAdx).ShouldBe(10);
        FirstReady(values, v => v.IndexTrend).ShouldBe(10);
        FirstReady(values, v => v.IndexVolPct).ShouldBe(8);
        FirstReady(values, v => v.VixPct).ShouldBe(6);
    }

    private static void Compare(int seed, ContextOptions options)
    {
        var days = ContextSeriesGenerator.Random(seed);
        var context = MarketContext.Create(days, options).Value;

        var actual = ContextRead.Days(context, days);
        var reference = ContextRead.Columns(ReferenceContext.Compute(days, options));

        context.Count.ShouldBe(days.Count);
        for (var d = 0; d < days.Count; d++)
        {
            var read = ContextRead.Fields(actual[d]);
            for (var f = 0; f < read.Length; f++)
            {
                if (!Tolerance.Same(reference[f][d], read[f]))
                {
                    Assert.Fail($"seed {seed}, day {d} ({days[d].Date}), {ContextRead.FieldNames[f]}: reference {reference[f][d]:R}, context {read[f]:R}");
                }
            }
        }
    }

    /// <summary>Consecutive weekdays that all carry a VIX and a breadth: a random walk with a steady range.</summary>
    private static List<ContextDay> FullDays(int count)
    {
        var random = new Random(77);
        var close = 1_000.0;
        return [.. Enumerable.Range(0, count).Select(i =>
        {
            close *= 1 + (random.NextDouble() - 0.5) * 0.02;
            return new ContextDay(ContextSeriesGenerator.Weekday(i), close * 1.004, close * 0.996, close, 12 + random.NextDouble() * 10, random.NextDouble() * 100);
        })];
    }

    /// <summary>The 1-based number of the first day for which the field is ready, or 0 when it never is.</summary>
    private static int FirstReady(List<ContextValues> values, Func<ContextValues, double> field)
    {
        var index = values.FindIndex(v => !double.IsNaN(field(v)));
        return index + 1;
    }
}
