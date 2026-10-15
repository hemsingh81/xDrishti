using Skender.Stock.Indicators;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// sma, ema, rsi and atr against Skender.Stock.Indicators 2.7.3 (a test-only dependency) on seeded random series. Prices are rounded to hundredths so the decimal quotes Skender takes carry exactly the doubles we feed.
/// <para>
/// Conventions found, none hidden by a loose tolerance (every comparison below is within 1e-9):
/// </para>
/// <list type="bullet">
/// <item><b>sma, ema</b> — identical: first value at bar n; the ema is seeded with the simple mean of the first n values.</item>
/// <item><b>rsi</b> — identical (first value at bar n+1, averages seeded with the simple mean of the first n changes, then Wilder) with one exception: when both averages are 0 Skender reports 100, we report 50 (nothing
/// moved). That case is pinned separately.</item>
/// <item><b>atr</b> — same Wilder recursion, different seed. Skender has no true range for the first bar (<c>TR₁</c> is null) and seeds the average with the mean of <c>TR₂ … TRₙ₊₁</c>, first value at bar n+1; we use
/// <c>TR₁ = high − low</c> and seed with the mean of <c>TR₁ … TRₙ</c>, first value at bar n. From bar n+1 on both follow <c>x = (x·(n−1) + TR)/n</c> on the same true ranges, so their difference shrinks by exactly
/// <c>(n−1)/n</c> per bar; that relation is asserted, along with both seeds.</item>
/// </list>
/// </summary>
public sealed class SkenderComparisonTests
{
    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 24)];

    private static readonly int[] Periods = SkenderHelpers.Periods;

    private static List<Bar> Rounded(int seed) => SkenderHelpers.Rounded(seed);

    private static List<Quote> Quotes(List<Bar> bars) => SkenderHelpers.Quotes(bars);

    private static double[] Run(IIndicator indicator, List<Bar> bars) => SkenderHelpers.Run(indicator, bars);

    private static double[] Skender(IEnumerable<double?> values) => SkenderHelpers.FromSkender(values);

    private static void ShouldAgree(double[] ours, double[] theirs, string label) => SkenderHelpers.ShouldAgree(ours, theirs, label);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Sma_and_ema_equal_skender(int seed)
    {
        var bars = Rounded(seed);
        var quotes = Quotes(bars);

        foreach (var n in Periods)
        {
            ShouldAgree(Run(new Sma(n), bars), Skender(quotes.GetSma(n).Select(r => r.Sma)), $"seed {seed}, sma({n})");
            ShouldAgree(Run(new Ema(n), bars), Skender(quotes.GetEma(n).Select(r => r.Ema)), $"seed {seed}, ema({n})");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Rsi_equals_skender_except_where_nothing_moved(int seed)
    {
        var bars = Rounded(seed);
        var quotes = Quotes(bars);

        foreach (var n in Periods)
        {
            var ours = Run(new Rsi(n), bars);
            var theirs = Skender(quotes.GetRsi(n).Select(r => r.Rsi));
            for (var i = 0; i < ours.Length; i++)
            {
                if (ours[i] == 50)
                {
                    theirs[i].ShouldBe(100.0, $"seed {seed}, rsi({n}), bar {i}: both averages are 0, which Skender reports as 100");
                    continue;
                }

                if (!Tolerance.Same(theirs[i], ours[i]))
                {
                    Assert.Fail($"seed {seed}, rsi({n}), bar {i}: Skender {theirs[i]:R}, ours {ours[i]:R}");
                }
            }
        }
    }

    [Fact]
    public void A_series_that_never_moves_is_fifty_for_us_and_one_hundred_for_skender()
    {
        var bars = Minutes.Closes(new DateOnly(2026, 3, 2), Enumerable.Repeat(10.0, 20).ToArray());

        var ours = Run(new Rsi(5), bars);
        var theirs = Skender(Quotes(bars).GetRsi(5).Select(r => r.Rsi));

        ours.Skip(5).ShouldAllBe(v => v == 50);
        theirs.Skip(5).ShouldAllBe(v => v == 100);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Atr_follows_the_same_recursion_as_skender_from_a_seed_one_bar_earlier(int seed)
    {
        var bars = Rounded(seed);
        var quotes = Quotes(bars);
        var tr = ReferenceIndicators.TrueRange(bars);

        foreach (var n in Periods)
        {
            var ours = Run(new Atr(n), bars);
            var theirs = Skender(quotes.GetAtr(n).Select(r => r.Atr));

            // the seeds: ours the mean of TR1..TRn at bar n, Skender's the mean of TR2..TRn+1 at bar n+1 (index n)
            ours.Take(n - 1).ShouldAllBe(v => double.IsNaN(v));
            Tolerance.Same(tr.Take(n).Average(), ours[n - 1]).ShouldBeTrue($"seed {seed}, atr({n}) seed");
            theirs.Take(n).ShouldAllBe(v => double.IsNaN(v));
            Tolerance.Same(tr.Skip(1).Take(n).Average(), theirs[n]).ShouldBeTrue($"seed {seed}, atr({n}) Skender seed");

            // after the seed bar the gap closes by (n - 1) / n per bar, exactly
            var gap = ours[n] - theirs[n];
            for (var i = n; i < bars.Count; i++)
            {
                var expected = gap * Math.Pow((n - 1.0) / n, i - n);
                if (Math.Abs((ours[i] - theirs[i]) - expected) > 1e-9 * Math.Max(1.0, Math.Abs(ours[i])))
                {
                    Assert.Fail($"seed {seed}, atr({n}), bar {i}: difference {ours[i] - theirs[i]:R}, expected {expected:R}");
                }
            }
        }
    }

    [Fact]
    public void Atr_coincides_with_skender_when_the_two_seeds_happen_to_be_equal()
    {
        // When the seeds are equal numbers the two series are one from bar n+1 on (the recursion and the true ranges are the same).
        var day = new DateOnly(2026, 3, 2);
        var bars = new List<Bar>
        {
            Minutes.Bar(day, Minutes.Open, 100, 102, 100, 101),       // TR1 = 2
            Minutes.Bar(day, Minutes.Open + 1, 101, 102, 100, 101),   // TR = 2
            Minutes.Bar(day, Minutes.Open + 2, 101, 103, 101, 102),   // TR = 2
            Minutes.Bar(day, Minutes.Open + 3, 102, 105, 102, 104),   // TR = 3
            Minutes.Bar(day, Minutes.Open + 4, 104, 106, 103, 105),   // TR = 3
            Minutes.Bar(day, Minutes.Open + 5, 105, 105, 102, 103),   // TR = 3
            Minutes.Bar(day, Minutes.Open + 6, 103, 108, 103, 107),   // TR = 5
        };

        // true ranges 2 2 2 3 3 3 5: our seed (2 + 2 + 2) / 3 = 2 at bar 3; Skender's (2 + 2 + 3) / 3 = 7/3 at bar 4, and ours at bar 4 is (2 * 2 + 3) / 3 = 7/3 as well
        var ours = Run(new Atr(3), bars);
        var theirs = Skender(Quotes(bars).GetAtr(3).Select(r => r.Atr));

        ours[2].ShouldBe(2.0, 1e-12);
        theirs[2].ShouldBeNaN();
        for (var i = 3; i < bars.Count; i++)
        {
            ours[i].ShouldBe(theirs[i], 1e-12);
        }
    }
}
