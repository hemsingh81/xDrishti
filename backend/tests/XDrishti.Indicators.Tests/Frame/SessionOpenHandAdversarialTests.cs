using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Frame;

/// <summary>
/// ISS-13, adversarial, 1-minute lane, hand-calculated: three sessions of three, three and two bars. For each of the twelve restarting features the history at the first, second and third bar of a session holds the
/// session's own bars only (lag k is NaN once k reaches the number of bars the session has closed), whatever the previous session left in the slots. The expected series are worked out below from the definitions.
/// <code>
/// bar    date  open  high  low  close  volume
/// M1     Mon   100   104   98   102    10
/// M2     Mon   102   108   101  106    20
/// M3     Mon   106   107   95   96     30
/// T1     Tue   110   115   109  112    40
/// T2     Tue   112   113   105  106    50
/// T3     Tue   106   120   104  118    60
/// W1     Wed   120   126   119  125    5
/// W2     Wed   125   125   118  119    15
/// </code>
/// Typical prices (H + L + C) / 3: M 304/3, 315/3, 298/3; T 336/3, 324/3, 342/3; W 370/3, 362/3. Monday: open 100, high 108, low 95, close 96, volume 60. Tuesday: open 110, high 120, low 104, close 118, volume 150.
/// </summary>
public sealed class SessionOpenHandAdversarialTests
{
    private static readonly double Nan = double.NaN;

    /// <summary>Index of the first bar of the session of each of the 8 bars.</summary>
    private static readonly int[] SessionStart = [0, 0, 0, 3, 3, 3, 6, 6];

    private static List<Bar> Bars() =>
    [
        Minutes.Bar(BarFixtures.Mon, Minutes.Open, 100, 104, 98, 102, 10),
        Minutes.Bar(BarFixtures.Mon, Minutes.Open + 1, 102, 108, 101, 106, 20),
        Minutes.Bar(BarFixtures.Mon, Minutes.Open + 2, 106, 107, 95, 96, 30),
        Minutes.Bar(BarFixtures.Tue, Minutes.Open, 110, 115, 109, 112, 40),
        Minutes.Bar(BarFixtures.Tue, Minutes.Open + 1, 112, 113, 105, 106, 50),
        Minutes.Bar(BarFixtures.Tue, Minutes.Open + 2, 106, 120, 104, 118, 60),
        Minutes.Bar(BarFixtures.Wed, Minutes.Open, 120, 126, 119, 125, 5),
        Minutes.Bar(BarFixtures.Wed, Minutes.Open + 1, 125, 125, 118, 119, 15),
    ];

    /// <summary>The value the feature has after each of the 8 bars (lag 0), worked out by hand; NaN where the feature is not ready.</summary>
    private static double[] Expected(string name) => name switch
    {
        // Σ(typical · volume) / Σvolume of the session so far
        "vwap" =>
        [
            304.0 / 3,
            (304.0 / 3 * 10 + 315.0 / 3 * 20) / 30,
            (304.0 / 3 * 10 + 315.0 / 3 * 20 + 298.0 / 3 * 30) / 60,
            336.0 / 3,
            (336.0 / 3 * 40 + 324.0 / 3 * 50) / 90,
            (336.0 / 3 * 40 + 324.0 / 3 * 50 + 342.0 / 3 * 60) / 150,
            370.0 / 3,
            (370.0 / 3 * 5 + 362.0 / 3 * 15) / 20,
        ],
        // the range is the first minute of the session (or_high(1) / or_low(1)): ready at the first bar, constant afterwards
        "or_high" => [104, 104, 104, 115, 115, 115, 126, 126],
        "or_low" => [98, 98, 98, 109, 109, 109, 119, 119],
        "today_open" => [100, 100, 100, 110, 110, 110, 120, 120],
        "today_high" => [104, 108, 108, 115, 115, 120, 126, 126],
        "today_low" => [98, 98, 95, 109, 105, 104, 119, 118],
        "prev_day_open" => [Nan, Nan, Nan, 100, 100, 100, 110, 110],
        "prev_day_high" => [Nan, Nan, Nan, 108, 108, 108, 120, 120],
        "prev_day_low" => [Nan, Nan, Nan, 95, 95, 95, 104, 104],
        "prev_day_close" => [Nan, Nan, Nan, 96, 96, 96, 118, 118],
        "prev_day_volume" => [Nan, Nan, Nan, 60, 60, 60, 150, 150],
        "gap_pct" => [Nan, Nan, Nan, (110.0 - 96) / 96 * 100, (110.0 - 96) / 96 * 100, (110.0 - 96) / 96 * 100, (120.0 - 118) / 118 * 100, (120.0 - 118) / 118 * 100],
        _ => throw new ArgumentException(name),
    };

    public static TheoryData<string, int> NamesAndDepths
    {
        get
        {
            var data = new TheoryData<string, int>();
            foreach (var name in AdversarialFrames.Twelve)
            {
                foreach (var depth in new[] { 1, 2, 4, 256 })
                {
                    data.Add(name, depth);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(NamesAndDepths))]
    public void The_history_of_a_session_feature_holds_the_bars_of_its_own_session_only_at_the_first_second_and_third_bar(string name, int depth)
    {
        var want = Expected(name);
        var frame = AdversarialFrames.Create([AdversarialFrames.TwelveSpec(name, Timeframe.M1)], depth);
        var bars = Bars();

        for (var n = 0; n < bars.Count; n++)
        {
            frame.Step(bars[n]);

            var barsInSession = n - SessionStart[n] + 1;
            for (var k = 0; k < depth; k++)
            {
                var expected = k < barsInSession ? want[n - k] : Nan;
                frame.At(0, k).ShouldBeNear(expected, $"{name} at bar {n + 1} lag {k} depth {depth}: the session has {barsInSession} bar(s)");
                (k == 0 ? frame.IsReady(0) : frame.IsReadyAt(0, k)).ShouldBe(!double.IsNaN(expected), $"{name} at bar {n + 1} lag {k}: readiness is NaN-ness");
            }
        }
    }

    [Fact]
    public void A_continuous_feature_beside_the_session_features_keeps_its_history_across_the_open()
    {
        // ema(2) of the closes 102 106 96 112 106 118 125 119: seeded with the mean of the first two (104), then ema + 2/3 (close - ema). close and ema(2) share the lane with the cleared features.
        var specs = new List<FeatureSpec>(AdversarialFrames.TwelveOn(Timeframe.M1)) { AdversarialFrames.Spec("ema", Timeframe.M1, 2), AdversarialFrames.Spec("close", Timeframe.M1) };
        var frame = AdversarialFrames.Create(specs, 4);
        var ema = specs.Count - 2;
        var close = specs.Count - 1;
        var closes = new[] { 102.0, 106, 96, 112, 106, 118, 125, 119 };
        var emas = new double[8];
        emas[0] = Nan;
        emas[1] = 104;
        for (var i = 2; i < 8; i++)
        {
            emas[i] = emas[i - 1] + 2.0 / 3 * (closes[i] - emas[i - 1]);
        }

        var bars = Bars();
        for (var n = 0; n < bars.Count; n++)
        {
            frame.Step(bars[n]);

            for (var k = 0; k < 4; k++)
            {
                frame.At(close, k).ShouldBeNear(n - k >= 0 ? closes[n - k] : Nan, $"close at bar {n + 1} lag {k}");
                frame.At(ema, k).ShouldBeNear(n - k >= 0 ? emas[n - k] : Nan, $"ema(2) at bar {n + 1} lag {k}");
            }
        }
    }

    [Fact]
    public void The_first_bar_of_the_first_session_ever_has_no_history_at_all()
    {
        var frame = AdversarialFrames.Create(AdversarialFrames.TwelveOn(Timeframe.M1), 4);

        frame.Step(Bars()[0]);

        for (var h = 0; h < frame.Count; h++)
        {
            for (var k = 1; k < 4; k++)
            {
                frame.At(h, k).ShouldBeNear(Nan, $"{frame.SpecOf(h).Name} lag {k} on the very first bar");
            }
        }
    }

    [Fact]
    public void A_lag_that_reaches_back_across_the_open_by_one_bar_or_by_all_of_them_is_never_known_until_the_session_has_that_many_bars()
    {
        // Tuesday has three bars: today_high is 115, 115, 120. Depth 8 so that the slots of Monday (104, 108, 108) are inside the ring: At(4) at T3 would be Monday's M2 by position, and must be NaN.
        var frame = AdversarialFrames.Create([AdversarialFrames.Spec("today_high", Timeframe.M1)], 8);
        var bars = Bars();
        for (var n = 0; n < 5; n++)
        {
            frame.Step(bars[n]);
        }

        // after T2: lags 0..7 = 115, 115, then NaN: Monday's 108 and 108 and 104 must not show through
        frame.ShouldHaveLags(0, [115, 115, Nan, Nan, Nan, Nan, Nan, Nan], "history");

        frame.Step(bars[5]);

        frame.ShouldHaveLags(0, [120, 115, 115, Nan, Nan, Nan, Nan, Nan], "history");
    }

    [Fact]
    public void A_zero_volume_first_bar_leaves_the_vwap_unready_and_its_history_unready_as_well()
    {
        // The first Tuesday bar trades no volume: vwap has no value (Σvolume = 0) until bar 2, whose typical price is the vwap; its lag 1 is the unready bar 1; at bar 3 lag 1 is bar 2's value; Monday's last vwap never shows.
        var frame = AdversarialFrames.Create([AdversarialFrames.Spec("vwap", Timeframe.M1)], 4);
        var bars = new List<Bar>
        {
            Minutes.Bar(BarFixtures.Mon, Minutes.Open, 100, 100, 100, 100, 10),
            Minutes.Bar(BarFixtures.Mon, Minutes.Open + 1, 100, 100, 100, 100, 10),
            Minutes.Bar(BarFixtures.Tue, Minutes.Open, 200, 210, 190, 200, 0),
            Minutes.Bar(BarFixtures.Tue, Minutes.Open + 1, 200, 203, 197, 200, 10),
            Minutes.Bar(BarFixtures.Tue, Minutes.Open + 2, 200, 209, 197, 203, 10),
        };

        foreach (var bar in bars.Take(3))
        {
            frame.Step(bar);
        }

        frame.ShouldHaveLags(0, [Nan, Nan, Nan, Nan], "history");

        frame.Step(bars[3]);

        // typical (203 + 197 + 200) / 3 = 200
        frame.ShouldHaveLags(0, [200, Nan, Nan, Nan], "history");

        frame.Step(bars[4]);

        // typical (209 + 197 + 203) / 3 = 203; vwap = (200 · 10 + 203 · 10) / 20 = 201.5
        frame.ShouldHaveLags(0, [201.5, 200, Nan, Nan], "history");
    }

    [Fact]
    public void An_opening_range_of_several_minutes_is_unready_for_the_first_bars_and_its_history_does_not_borrow_the_previous_range()
    {
        // or_high(3) on 1m: the highest high of 09:15, 09:16, 09:17. Monday's range is ready at bar 3 (109, bar 4 starts after the window and does not change it); Tuesday's is ready at its third bar, 130, and its lags:
        // bar 3 -> [130, NaN (bar 2), NaN (bar 1)], bar 4 -> [130, 130, NaN, NaN], bar 5 -> [130, 130, 130, NaN, NaN]. A stale slot would show Monday's 109 at bar 3 lag 1.
        var frame = AdversarialFrames.Create([AdversarialFrames.Spec("or_high", Timeframe.M1, 3)], 6);
        var bars = new List<Bar>
        {
            Minutes.Bar(BarFixtures.Mon, Minutes.Open, 100, 105, 99, 101),
            Minutes.Bar(BarFixtures.Mon, Minutes.Open + 1, 101, 109, 100, 108),
            Minutes.Bar(BarFixtures.Mon, Minutes.Open + 2, 108, 108, 104, 105),
            Minutes.Bar(BarFixtures.Mon, Minutes.Open + 3, 105, 200, 104, 105),
            Minutes.Bar(BarFixtures.Tue, Minutes.Open, 120, 125, 119, 124),
            Minutes.Bar(BarFixtures.Tue, Minutes.Open + 1, 124, 130, 121, 128),
            Minutes.Bar(BarFixtures.Tue, Minutes.Open + 2, 128, 128, 120, 121),
            Minutes.Bar(BarFixtures.Tue, Minutes.Open + 3, 121, 300, 120, 121),
            Minutes.Bar(BarFixtures.Tue, Minutes.Open + 4, 121, 121, 100, 101),
        };
        var seen = new List<double[]>();
        foreach (var bar in bars)
        {
            frame.Step(bar);
            seen.Add(AdversarialFrames.Lags(frame, 0));
        }

        AdversarialFrames.ShouldBeLags(seen[3], [109, 109, Nan, Nan, Nan, Nan], "or_high(3) after bar 3");
        AdversarialFrames.ShouldBeLags(seen[4], [Nan, Nan, Nan, Nan, Nan, Nan], "or_high(3) after bar 4");
        AdversarialFrames.ShouldBeLags(seen[5], [Nan, Nan, Nan, Nan, Nan, Nan], "or_high(3) after bar 5");
        AdversarialFrames.ShouldBeLags(seen[6], [130, Nan, Nan, Nan, Nan, Nan], "or_high(3) after bar 6");
        AdversarialFrames.ShouldBeLags(seen[7], [130, 130, Nan, Nan, Nan, Nan], "or_high(3) after bar 7");
        AdversarialFrames.ShouldBeLags(seen[8], [130, 130, 130, Nan, Nan, Nan], "or_high(3) after bar 8");
    }

}
