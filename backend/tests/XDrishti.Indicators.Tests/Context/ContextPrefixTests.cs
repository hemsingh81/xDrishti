using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Context;

/// <summary>
/// Criterion 9b, the core guarantee of the context: the values of the first <i>k</i> days are identical, bit for bit, when the series is cut after <i>k</i> days, when anything at all follows them, and whether the
/// context is built in one call or day by day. Over 220 seeds; the options alternate between the defaults and small periods so that the short series pass through every state.
/// </summary>
public sealed class ContextPrefixTests
{
    private static readonly ContextOptions[] Variants =
    [
        new ContextOptions(),
        new ContextOptions(TrendSmaPeriod: 8, TrendSlopeDays: 3, AdxPeriod: 5, AdxMin: 20, AtrPeriod: 5, PercentileWindow: 20),
        new ContextOptions(TrendSmaPeriod: 5, TrendSlopeDays: 1, AdxPeriod: 3, AdxMin: 0, AtrPeriod: 2, PercentileWindow: 7, LowVolBelow: 40, HighVolFrom: 60),
        new ContextOptions(TrendSmaPeriod: 12, TrendSlopeDays: 6, AdxPeriod: 4, AdxMin: 30, AtrPeriod: 9, PercentileWindow: 30),
    ];

    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 220)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_values_of_the_first_k_days_do_not_depend_on_the_days_after_them(int seed)
    {
        var options = Variants[seed % Variants.Length];
        var days = Series(seed);
        var cut = new Random(seed * 7919).Next(1, days.Count + 1);
        var prefix = days.Take(cut).ToList();

        var full = ContextRead.Days(MarketContext.Create(days, options).Value, days);
        var cutOff = ContextRead.Days(MarketContext.Create(prefix, options).Value, prefix);
        var appended = MarketContext.Create([], options).Value;
        foreach (var day in prefix)
        {
            appended.Append(day).IsSuccess.ShouldBeTrue();
        }

        var viaAppend = ContextRead.Days(appended, prefix);

        for (var d = 0; d < cut; d++)
        {
            ContextRead.FirstDifference(full[d], cutOff[d]).ShouldBeNull($"seed {seed}, cut {cut}, day {d}: cut series vs the whole series");
            ContextRead.FirstDifference(cutOff[d], viaAppend[d]).ShouldBeNull($"seed {seed}, cut {cut}, day {d}: Create vs Append");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void A_different_future_after_day_k_changes_nothing_that_was_read_before_it(int seed)
    {
        var options = Variants[seed % Variants.Length];
        var days = Series(seed);
        var cut = new Random(seed * 31 + 7).Next(1, days.Count + 1);
        var prefix = days.Take(cut).ToList();
        var original = MarketContext.Create(days, options).Value;
        var other = MarketContext.Create(prefix.Concat(OtherFuture(seed, prefix[^1].Date)), options).Value;
        var originalValues = ContextRead.Days(original, prefix);
        var otherValues = ContextRead.Days(other, prefix);

        for (var d = 0; d < cut; d++)
        {
            ContextRead.FirstDifference(originalValues[d], otherValues[d]).ShouldBeNull($"seed {seed}, cut {cut}, day {d}");
        }

        // and what a reader at any date up to and including the first date after the cut sees is the same too
        for (var d = 0; d < cut; d++)
        {
            original.TryAsOf(prefix[d].Date, out var a).ShouldBe(other.TryAsOf(prefix[d].Date, out var b));
            ContextRead.FirstDifference(a, b).ShouldBeNull($"seed {seed}, reader at {prefix[d].Date}");
        }

        var firstOfFuture = prefix[^1].Date.AddDays(1);
        original.TryAsOf(firstOfFuture, out var lastA).ShouldBeTrue();
        other.TryAsOf(firstOfFuture, out var lastB).ShouldBeTrue();
        ContextRead.FirstDifference(lastA, lastB).ShouldBeNull($"seed {seed}, the reader on the first date after the cut");
    }

    [Fact]
    public void Appending_a_day_never_changes_the_values_already_read()
    {
        var days = ContextSeriesGenerator.Random(4, 200, 200);
        var context = MarketContext.Create([], Variants[1]).Value;
        var seen = new List<ContextValues>();

        foreach (var day in days)
        {
            context.Append(day).IsSuccess.ShouldBeTrue();
            context.TryAsOf(day.Date.AddDays(1), out var today).ShouldBeTrue();
            seen.Add(today);

            // everything read earlier still reads the same now
            if (seen.Count % 10 == 0)
            {
                for (var d = 0; d < seen.Count; d++)
                {
                    context.TryAsOf(days[d].Date.AddDays(1), out var again).ShouldBeTrue();
                    ContextRead.FirstDifference(seen[d], again).ShouldBeNull($"day {d} after {seen.Count} appended");
                }
            }
        }
    }

    /// <summary>Long enough for the defaults to be ready (the first variant) or short for the small periods.</summary>
    private static List<ContextDay> Series(int seed) =>
        seed % Variants.Length == 0 ? ContextSeriesGenerator.Random(seed, 300, 700) : ContextSeriesGenerator.Random(seed, 60, 300);

    /// <summary>Another future for the same past: more days after the date, with prices five times higher, a VIX at its extremes and no breadth — nothing like the original continuation.</summary>
    private static IEnumerable<ContextDay> OtherFuture(int seed, DateOnly lastDate)
    {
        var other = ContextSeriesGenerator.Random(seed + 10_000, 80, 200);
        var shift = lastDate.DayNumber + 1 - other[0].Date.DayNumber;
        foreach (var day in other)
        {
            yield return day with { Date = day.Date.AddDays(shift), IndexHigh = day.IndexHigh * 5, IndexLow = day.IndexLow * 5, IndexClose = day.IndexClose * 5, VixClose = day.VixClose is null ? null : 85.0, Breadth = null };
        }
    }
}
