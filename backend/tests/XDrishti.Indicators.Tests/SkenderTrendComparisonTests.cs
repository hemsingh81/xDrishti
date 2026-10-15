using Skender.Stock.Indicators;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;
using static XDrishti.Indicators.Tests.Support.SkenderHelpers;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// adx/plus_di/minus_di, macd_*, bb_*, roc and supertrend against Skender.Stock.Indicators 2.7.3 (ADR 0032) on seeded random series with prices rounded to hundredths. Everything below is within 1e-9, and
/// every difference of convention is asserted exactly; none is absorbed by a tolerance.
/// <list type="bullet">
/// <item><b>adx, plus_di, minus_di</b> — identical: the DIs from bar n+1 with Wilder's sum seeded by the first n values, the ADX at bar 2n seeded with the mean of the first n DX values.</item>
/// <item><b>macd</b> — identical: both EMAs and the signal are seeded with the simple mean of their first values; line at bar slow, signal at bar slow+g−1.</item>
/// <item><b>bb</b> — identical, with the population standard deviation; Skender's <c>Width</c> is (upper − lower)/middle.</item>
/// <item><b>roc</b> — identical, first value at bar n+1.</item>
/// <item><b>supertrend</b> — two differences of convention remain, both pinned exactly (and one tie-breaking detail, below). (1) The ATR seed: Skender's first value is at bar n+1 and its ATR is seeded one bar later than ours (ADR 0031), so values differ by the multiplier
/// times the ATR difference unless the seeds happen to be equal; ours is ready at bar n. (2) Skender starts with direction up when close ≥ (high+low)/2, ours always up; the two then agree from the first bar whose close
/// is beyond a final band. The final bands and the flip rule (the close against the <i>current</i> final band) are the same, so on series with equal ATR seeds and a start both agree on, the output is identical. The tie-break: a close exactly on the final lower band flips Skender's up trend down, ours (and TradingView's) stays up.</item>
/// </list>
/// </summary>
public sealed class SkenderTrendComparisonTests
{
    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 24)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Adx_and_the_directional_indicators_equal_skender(int seed)
    {
        var bars = Rounded(seed);
        var quotes = Quotes(bars);

        foreach (var n in Periods)
        {
            var theirs = quotes.GetAdx(n).ToList();

            ShouldAgree(Run(new PlusDi(n), bars), FromSkender(theirs.Select(r => r.Pdi)), $"seed {seed}, plus_di({n})");
            ShouldAgree(Run(new MinusDi(n), bars), FromSkender(theirs.Select(r => r.Mdi)), $"seed {seed}, minus_di({n})");
            ShouldAgree(Run(new Adx(n), bars), FromSkender(theirs.Select(r => r.Adx)), $"seed {seed}, adx({n})");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Macd_equals_skender(int seed)
    {
        var bars = Rounded(seed);
        var quotes = Quotes(bars);

        foreach (var (fast, slow, signal) in new[] { (3, 6, 4), (12, 26, 9), (5, 35, 5) })
        {
            var theirs = quotes.GetMacd(fast, slow, signal).ToList();
            var label = $"seed {seed}, macd({fast},{slow},{signal})";

            ShouldAgree(Run(new MacdLine(fast, slow, signal), bars), FromSkender(theirs.Select(r => r.Macd)), label + " line");
            ShouldAgree(Run(new MacdSignal(fast, slow, signal), bars), FromSkender(theirs.Select(r => r.Signal)), label + " signal");
            ShouldAgree(Run(new MacdHist(fast, slow, signal), bars), FromSkender(theirs.Select(r => r.Histogram)), label + " histogram");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Bollinger_bands_equal_skender_with_the_population_deviation(int seed)
    {
        var bars = Rounded(seed);
        var quotes = Quotes(bars);

        foreach (var (n, k) in new[] { (2, 2.0), (5, 1.5), (20, 2.0), (14, 3.0) })
        {
            var theirs = quotes.GetBollingerBands(n, k).ToList();
            var label = $"seed {seed}, bb({n},{k})";

            ShouldAgree(Run(new BbUpper(n, k), bars), FromSkender(theirs.Select(r => r.UpperBand)), label + " upper");
            ShouldAgree(Run(new BbMiddle(n, k), bars), FromSkender(theirs.Select(r => r.Sma)), label + " middle");
            ShouldAgree(Run(new BbLower(n, k), bars), FromSkender(theirs.Select(r => r.LowerBand)), label + " lower");
            ShouldAgree(Run(new BbWidth(n, k), bars), FromSkender(theirs.Select(r => r.Width)), label + " width");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Roc_equals_skender(int seed)
    {
        var bars = Rounded(seed);
        var quotes = Quotes(bars);

        foreach (var n in Periods)
        {
            ShouldAgree(Run(new Roc(n), bars), FromSkender(quotes.GetRoc(n).Select(r => r.Roc)), $"seed {seed}, roc({n})");
        }
    }

    [Fact]
    public void Roc_with_a_zero_base_is_not_ready_for_us_and_for_skender()
    {
        var bars = Minutes.Closes(new DateOnly(2026, 3, 2), 4, 0, 5, 6, 7);

        var ours = Run(new Roc(2), bars);
        var theirs = FromSkender(Quotes(bars).GetRoc(2).Select(r => r.Roc));

        ours[3].ShouldBeNaN();
        theirs[3].ShouldBeNaN();
        ours[4].ShouldBe(theirs[4], 1e-12);
    }

    // ---- Supertrend ------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A series whose first n+1 true ranges make our ATR seed (mean of TR₁…TRₙ) and Skender's (mean of TR₂…TRₙ₊₁) the same number, and whose bar n+1 narrows both bands (they are reset there): n flat-range bars with a
    /// constant close, one narrower bar (closing at <paramref name="joinClose"/>, which is below its mid price 100 for the series that make Skender start down), then a seeded random walk.
    /// </summary>
    private static List<Bar> AlignedSeries(int seed, int n, int total = 400, double joinClose = 100)
    {
        var random = new Random(seed);
        var day = new DateOnly(2026, 3, 2);
        var bars = new List<Bar>();
        for (var i = 0; i < n; i++)
        {
            bars.Add(Minutes.Bar(day, Minutes.Open + i, 100, 101, 99, 100));
        }

        bars.Add(Minutes.Bar(day, Minutes.Open + n, 100, 100.5, 99.5, joinClose));
        var price = joinClose;
        for (var i = n + 1; i < total; i++)
        {
            var open = price;
            var close = Math.Round(open + (random.NextDouble() - 0.5) * 2, 2);
            var high = Math.Round(Math.Max(open, close) + random.NextDouble() * 0.8, 2);
            var low = Math.Round(Math.Min(open, close) - random.NextDouble() * 0.8, 2);
            bars.Add(Minutes.Bar(day, Minutes.Open + i, open, high, low, close));
            price = close;
        }

        return bars;
    }

    /// <summary>Skender's line and its direction (+1 when the lower band is shown, −1 when the upper band is).</summary>
    private static (double[] Value, double[] Direction) SkenderSupertrend(List<Bar> bars, int n, double multiplier)
    {
        var results = Quotes(bars).GetSuperTrend(n, multiplier).ToList();
        var value = FromSkender(results.Select(r => r.SuperTrend));
        var direction = results.Select(r => r.SuperTrend is null ? double.NaN : r.LowerBand is not null ? 1.0 : -1.0).ToArray();
        return (value, direction);
    }

    public static TheoryData<int, int, double> SupertrendCases
    {
        get
        {
            var data = new TheoryData<int, int, double>();
            foreach (var seed in Enumerable.Range(1, 24))
            {
                foreach (var (n, multiplier) in new[] { (5, 2.0), (10, 3.0), (3, 0.5) })
                {
                    data.Add(seed, n, multiplier);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SupertrendCases))]
    public void Skender_supertrend_has_its_first_value_one_bar_after_ours_and_follows_the_reference_exactly(int seed, int n, double multiplier)
    {
        var bars = AlignedSeries(seed, n);

        var theirs = SkenderSupertrend(bars, n, multiplier);
        var reference = ReferenceTrend.Supertrend(bars, n, multiplier, ReferenceTrend.SupertrendStart.Skender);
        var ours = Run(new Supertrend(n, multiplier), bars);

        // first value: bar n for us (index n-1), bar n+1 for Skender (index n)
        ours.Take(n - 1).ShouldAllBe(v => double.IsNaN(v));
        double.IsNaN(ours[n - 1]).ShouldBeFalse();
        theirs.Value.Take(n).ShouldAllBe(v => double.IsNaN(v));
        double.IsNaN(theirs.Value[n]).ShouldBeFalse();

        // Skender == the reference recursion with the close >= hl2 start (ATR seeds equal by construction)
        ShouldAgree(reference.Value, theirs.Value, $"seed {seed}, supertrend({n},{multiplier}) value");
        ShouldAgree(reference.Direction, theirs.Direction, $"seed {seed}, supertrend({n},{multiplier}) direction");
    }

    [Theory]
    [MemberData(nameof(SupertrendCases))]
    public void Our_supertrend_equals_skender_exactly_when_the_atr_seeds_are_equal_and_both_start_up(int seed, int n, double multiplier)
    {
        var bars = AlignedSeries(seed, n);
        var theirs = SkenderSupertrend(bars, n, multiplier);

        var ours = Run(new Supertrend(n, multiplier), bars);
        var oursDirection = Run(new SupertrendDirection(n, multiplier), bars);

        ShouldAgree(ours.Skip(n).ToArray(), theirs.Value.Skip(n).ToArray(), $"seed {seed}, supertrend({n},{multiplier}) value");
        ShouldAgree(oursDirection.Skip(n).ToArray(), theirs.Direction.Skip(n).ToArray(), $"seed {seed}, supertrend({n},{multiplier}) direction");
    }

    [Theory]
    [MemberData(nameof(SupertrendCases))]
    public void When_skender_starts_down_we_differ_only_until_a_close_is_beyond_a_final_band(int seed, int n, double multiplier)
    {
        // Skender starts at bar n+1 with the close below the mid price: down, on the upper band. Ours is up there (it started at bar n) and the close is above our lower band.
        var bars = AlignedSeries(seed, n, joinClose: 99.8);
        var theirs = SkenderSupertrend(bars, n, multiplier);
        var ours = Run(new Supertrend(n, multiplier), bars);
        var oursDirection = Run(new SupertrendDirection(n, multiplier), bars);
        theirs.Direction[n].ShouldBe(-1.0, $"seed {seed}: Skender starts down");
        oursDirection[n].ShouldBe(1.0, $"seed {seed}: we are still up");

        // the bands are the same on both sides, so the two trends differ over an initial stretch only, and agree (value and direction) from the first bar where one of them flips onto the other
        var agreeFrom = Enumerable.Range(n, bars.Count - n).First(i => oursDirection[i] == theirs.Direction[i]);
        for (var i = n; i < agreeFrom; i++)
        {
            oursDirection[i].ShouldBe(1.0, $"seed {seed}, bar {i}: we stay up while Skender stays down");
            theirs.Direction[i].ShouldBe(-1.0, $"seed {seed}, bar {i}");
        }

        (oursDirection[agreeFrom - 1] != oursDirection[agreeFrom] || theirs.Direction[agreeFrom - 1] != theirs.Direction[agreeFrom])
            .ShouldBeTrue($"seed {seed}: they meet because one of them flipped at bar {agreeFrom}");
        ShouldAgree(ours.Skip(agreeFrom).ToArray(), theirs.Value.Skip(agreeFrom).ToArray(), $"seed {seed}, supertrend({n},{multiplier}) value");
        ShouldAgree(oursDirection.Skip(agreeFrom).ToArray(), theirs.Direction.Skip(agreeFrom).ToArray(), $"seed {seed}, supertrend({n},{multiplier}) direction");
    }

    [Fact]
    public void A_close_exactly_on_the_final_lower_band_flips_skenders_up_trend_down_but_not_ours()
    {
        // Prices on a quarter grid make exact ties possible. n = 2, multiplier 0.5, bars as in AlignedSeries (equal ATR seeds, both start up).
        const int n = 2;
        const double multiplier = 0.5;
        var tied = 0;
        foreach (var seed in Enumerable.Range(1, 300))
        {
            var bars = GridSeries(seed, n);
            var theirs = SkenderSupertrend(bars, n, multiplier);
            var withTie = ReferenceTrend.Supertrend(bars, n, multiplier, ReferenceTrend.SupertrendStart.Skender, tieFlipsDown: true);
            var ours = Run(new Supertrend(n, multiplier), bars);
            var oursDirection = Run(new SupertrendDirection(n, multiplier), bars);

            // Skender is the reference with the tie-break, on every series; the first bar where we differ from Skender is such a tie
            ShouldAgree(withTie.Value, theirs.Value, $"seed {seed} value");
            ShouldAgree(withTie.Direction, theirs.Direction, $"seed {seed} direction");
            var firstDifference = Enumerable.Range(n, bars.Count - n).FirstOrDefault(i => !Tolerance.Same(ours[i], theirs.Value[i]) || oursDirection[i] != theirs.Direction[i], -1);
            if (firstDifference >= 0)
            {
                tied++;
                theirs.Direction[firstDifference].ShouldBe(-1.0, $"seed {seed}: Skender flips down at bar {firstDifference}");
                oursDirection[firstDifference].ShouldBe(1.0, $"seed {seed}: we stay up at bar {firstDifference}");
                bars[firstDifference].Close.ShouldBe(ours[firstDifference], 1e-12, $"seed {seed}: the close is exactly on our final lower band");
            }
        }

        tied.ShouldBeGreaterThan(0, "ties must occur on the quarter grid");
    }

    /// <summary>The flat start of <see cref="AlignedSeries"/> followed by a random walk whose prices (and so true ranges and bands) sit on a quarter grid, so a close can land exactly on a band.</summary>
    private static List<Bar> GridSeries(int seed, int n, int total = 40)
    {
        var random = new Random(seed);
        var day = new DateOnly(2026, 3, 2);
        var bars = new List<Bar>();
        for (var i = 0; i < n; i++)
        {
            bars.Add(Minutes.Bar(day, Minutes.Open + i, 100, 101, 99, 100));
        }

        bars.Add(Minutes.Bar(day, Minutes.Open + n, 100, 100.5, 99.5, 100));
        var price = 100.0;
        for (var i = n + 1; i < total; i++)
        {
            var open = price;
            var close = open + random.Next(-2, 3) * 0.25;
            var high = Math.Max(open, close) + random.Next(0, 3) * 0.25;
            var low = Math.Min(open, close) - random.Next(0, 3) * 0.25;
            bars.Add(Minutes.Bar(day, Minutes.Open + i, open, high, low, close));
            price = close;
        }

        return bars;
    }

    [Fact]
    public void With_unequal_atr_seeds_the_first_common_bar_differs_by_the_multiplier_times_the_atr_difference()
    {
        const int n = 5;
        const double multiplier = 2;
        var verified = 0;
        foreach (var seed in Enumerable.Range(1, 24))
        {
            var bars = Rounded(seed).Take(60).ToList();
            var trueRange = ReferenceIndicators.TrueRange(bars);
            var oursSeed = trueRange.Take(n).Average();                 // mean of TR1..TRn, at bar n
            var theirsSeed = trueRange.Skip(1).Take(n).Average();       // mean of TR2..TRn+1, at bar n+1
            var theirs = SkenderSupertrend(bars, n, multiplier);
            var ours = Run(new Supertrend(n, multiplier), bars);
            var oursDirection = Run(new SupertrendDirection(n, multiplier), bars);

            // At the first bar Skender has a value (index n) the bands are, on both sides, the basic lower band when the trend is up and our lower band was raised (Skender initialises it there).
            var middle = (bars[n].High + bars[n].Low) / 2;
            var oursAtr = (oursSeed * (n - 1) + trueRange[n]) / n;
            if (oursDirection[n] > 0 && theirs.Direction[n] > 0 && Tolerance.Same(middle - multiplier * oursAtr, ours[n]) && Tolerance.Same(middle - multiplier * theirsSeed, theirs.Value[n]))
            {
                (ours[n] - theirs.Value[n]).ShouldBe(multiplier * (theirsSeed - oursAtr), 1e-9, $"seed {seed}");
                verified++;
            }
        }

        verified.ShouldBeGreaterThanOrEqualTo(3, "the relation must be exercised on several series");
    }
}
