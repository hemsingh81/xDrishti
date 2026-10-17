using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// The per-session features restart in the frame, not only in the indicator (ADR 0035): on an intraday timeframe <c>vwap</c>, <c>or_high</c>, <c>or_low</c>, <c>today_*</c>, <c>prev_day_*</c> and <c>gap_pct</c> keep the
/// value history of the <b>current session only</b>, and a lane that has not yet closed a bar of the new date is <i>pending</i>: not ready at any lag. Every expectation here is worked out by hand from the definitions
/// (running means of flat bars, the first and last prices of a session), never from the output of the frame; the few ring slots that the old frame left over from the day before are named in the comments.
/// </summary>
public sealed class FrameSessionRestartTests
{
    private const double N = double.NaN;

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
    // H1: one-minute bars, three sessions of three bars. Flat bars (open = high = low = close) of volume 10, so vwap is the running mean of the closes of the session.
    //   bar:        M1   M2   M3  | T1   T2   T3  | W1  W2  W3
    //   close:      100  98   96  | 122  121  119 | 90  92  94
    //   vwap:       100  99   98  | 122  121.5  362/3 | 90  91  92
    //   today_high: 100  100  100 | 122  122  122 | 90  92  94
    //   prev close: -    -    -   | 96   96   96  | 119 119 119
    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static readonly double[] H1Closes = [100, 98, 96, 122, 121, 119, 90, 92, 94];

    /// <summary>The value k closed bars ago, lag 0 first: nothing before the session's first bar.</summary>
    private static readonly double[][] H1Vwap =
    [
        [100, N, N, N], [99, 100, N, N], [98, 99, 100, N],
        [122, N, N, N], [121.5, 122, N, N], [362.0 / 3, 121.5, 122, N],
        [90, N, N, N], [91, 90, N, N], [92, 91, 90, N],
    ];

    private static readonly double[][] H1TodayOpen =
    [
        [100, N, N, N], [100, 100, N, N], [100, 100, 100, N],
        [122, N, N, N], [122, 122, N, N], [122, 122, 122, N],
        [90, N, N, N], [90, 90, N, N], [90, 90, 90, N],
    ];

    private static readonly double[][] H1TodayHigh =
    [
        [100, N, N, N], [100, 100, N, N], [100, 100, 100, N],
        [122, N, N, N], [122, 122, N, N], [122, 122, 122, N],
        [90, N, N, N], [92, 90, N, N], [94, 92, 90, N],
    ];

    /// <summary>The first session has no session before it: not ready at any lag. Tuesday's previous close is Monday's last (96), Wednesday's is Tuesday's last (119).</summary>
    private static readonly double[][] H1PreviousClose =
    [
        [N, N, N, N], [N, N, N, N], [N, N, N, N],
        [96, N, N, N], [96, 96, N, N], [96, 96, 96, N],
        [119, N, N, N], [119, 119, N, N], [119, 119, 119, N],
    ];

    /// <summary>gap = (open of the day - previous close) / previous close · 100: Tuesday (122 - 96) / 96, Wednesday (90 - 119) / 119.</summary>
    private static double[][] H1Gap()
    {
        var tuesday = 26.0 / 96 * 100;
        var wednesday = -29.0 / 119 * 100;
        return
        [
            [N, N, N, N], [N, N, N, N], [N, N, N, N],
            [tuesday, N, N, N], [tuesday, tuesday, N, N], [tuesday, tuesday, tuesday, N],
            [wednesday, N, N, N], [wednesday, wednesday, N, N], [wednesday, wednesday, wednesday, N],
        ];
    }

    /// <summary>ema(2) is continuous: alpha = 2/3, seeded with the mean of the first two closes (99), and it runs on across the sessions, so its history is by position.</summary>
    private static readonly double[] H1Ema =
    [
        N, 99, 97,
        341.0 / 3,          // 97 + 2/3 · (122 − 97)
        1067.0 / 9,         // 341/3 + 2/3 · (121 − 341/3)
        3209.0 / 27,        // 1067/9 + 2/3 · (119 − 1067/9)
        8069.0 / 81,        // 3209/27 + 2/3 · (90 − 3209/27)
        22973.0 / 243,
        68657.0 / 729,
    ];

    private static List<FeatureSpec> H1Specs() =>
    [
        new("vwap"),
        new("today_open"),
        new("today_high"),
        new("prev_day_close"),
        new("gap_pct"),
        new("ema", Timeframe.M1, PriceSource.Close, 2),
    ];

    private static List<Bar> H1Bars()
    {
        var days = new[] { BarFixtures.Mon, BarFixtures.Tue, BarFixtures.Wed };
        var bars = new List<Bar>();
        for (var i = 0; i < H1Closes.Length; i++)
        {
            bars.Add(Minutes.Close(days[i / 3], Minutes.Open + i % 3, H1Closes[i]));
        }

        return bars;
    }

    private static double Expect(double[] row, int lag) => lag < row.Length ? row[lag] : N;

    private static void ShouldMatch(FeatureFrame frame, int handle, double want, int lag, string label)
    {
        var got = frame.At(handle, lag);
        if (!Tolerance.Same(want, got) || frame.IsReadyAt(handle, lag) == double.IsNaN(want))
        {
            Assert.Fail($"{label}, lag {lag}: expected {want:R}, At is {got:R} (ready {frame.IsReadyAt(handle, lag)})");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(256)]
    public void H1_the_history_of_a_session_feature_is_the_current_session_only_at_every_lag(int depth)
    {
        var frame = FeatureFrame.Create(H1Specs(), new FrameOptions(HistoryDepth: depth)).Value;
        var tables = new[] { H1Vwap, H1TodayOpen, H1TodayHigh, H1PreviousClose, H1Gap() };
        var names = new[] { "vwap", "today_open", "today_high", "prev_day_close", "gap_pct" };
        var bars = H1Bars();

        for (var step = 0; step < bars.Count; step++)
        {
            frame.Step(bars[step]);
            for (var lag = 0; lag < depth; lag++)
            {
                for (var h = 0; h < tables.Length; h++)
                {
                    ShouldMatch(frame, h, Expect(tables[h][step], lag), lag, $"depth {depth}, step {step + 1}, {names[h]}");
                }

                // the continuous feature next to them runs on across the sessions: the value lag bars ago by position (the old slots must not be cleared)
                ShouldMatch(frame, 5, step - lag >= 0 ? H1Ema[step - lag] : N, lag, $"depth {depth}, step {step + 1}, ema(2)");
            }
        }
    }

    [Fact]
    public void H1_at_the_first_bar_of_a_session_the_slots_of_the_day_before_are_not_read_one_bar_back()
    {
        // the old frame showed, at T1: vwap[1] = 98 and today_open[1] = 100 (Monday's last values); at W1: prev_day_close[1] = 96 and gap_pct[1] = 27.08 (Tuesday's); at T3 lag 3: 98 again
        var frame = FeatureFrame.Create(H1Specs(), new FrameOptions(HistoryDepth: 4)).Value;
        var bars = H1Bars();
        for (var i = 0; i < 4; i++)
        {
            frame.Step(bars[i]);
        }

        frame.At(0, 0).ShouldBe(122.0);
        frame.At(0, 1).ShouldBeNaN("vwap[1] at the first bar of Tuesday is not Monday's 98");
        frame.At(1, 1).ShouldBeNaN("today_open[1] is not Monday's 100");
        frame.At(5, 1).ShouldBe(97.0, "ema(2) is continuous: one bar back is Monday's last, 97");

        frame.Step(bars[4]);
        frame.Step(bars[5]);
        frame.At(0, 2).ShouldBe(122.0);
        frame.At(0, 3).ShouldBeNaN("lag 3 at Tuesday's third bar would be Monday's last bar (98)");

        frame.Step(bars[6]);
        frame.At(3, 0).ShouldBe(119.0);
        frame.At(3, 1).ShouldBeNaN("prev_day_close[1] at the first bar of Wednesday is not the 96 of Tuesday's first bar");
        frame.At(4, 0).ShouldBe(-29.0 / 119 * 100, 1e-9);
        frame.At(4, 1).ShouldBeNaN("gap_pct[1] is not Tuesday's 27.08");

        frame.Step(bars[7]);
        frame.Step(bars[8]);
        frame.At(2, 1).ShouldBe(92.0);
        frame.At(2, 2).ShouldBe(90.0);
        frame.At(2, 3).ShouldBeNaN("today_high lag 3 at Wednesday's third bar would be Tuesday's 122");
        frame.At(5, 1).ShouldBe(22973.0 / 243, 1e-9);
    }

    [Fact]
    public void H1_a_deep_history_has_nothing_beyond_the_session()
    {
        var frame = FeatureFrame.Create(H1Specs(), new FrameOptions(HistoryDepth: 256)).Value;
        var bars = H1Bars();
        for (var i = 0; i < 7; i++)
        {
            frame.Step(bars[i]);
        }

        // first bar of Wednesday: no lag from 1 to 255 reads anything of the two sessions before, for the five session features
        for (var h = 0; h < 5; h++)
        {
            for (var lag = 1; lag < 256; lag++)
            {
                frame.IsReadyAt(h, lag).ShouldBeFalse($"handle {h}, lag {lag}");
            }
        }

        // the continuous feature still sees the six bars before it, and ema(2) is not ready only at the very first one
        frame.IsReadyAt(5, 5).ShouldBeTrue();
        frame.At(5, 5).ShouldBe(99.0);
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
    // H2: a coarser lane is pending until it closes its first bar of the date. Three sessions of 30 flat one-minute bars (09:15 to 09:44): the closes of day d are base + i for minute i (base 100, 200, 300), volume 10.
    // Two 15-minute bars a day: B1 = minutes 0-14 (open base, high base+14, low base, close base+14), B2 = minutes 15-29 (open base+15, high = close = base+29, low base+15).
    //   typical price of B1 = (3·base + 28)/3, of B2 = (3·base + 73)/3, equal volumes, so vwap after B1 = base + 28/3 and after B2 = base + 101/6.
    //   15m close after B1/B2: base+14, base+29; prev_day_close of day d after B1: base(d-1)+29; ema(2) of the 15m closes: seed (114 + 129)/2 = 121.5, then alpha = 2/3.
    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static readonly double[] Bases = [100, 200, 300];

    /// <summary>The 15m ema(2) after B1 and B2 of each of the three days: not ready, 121.5; 1099/6, 3847/18; 15151/54, 50683/162.</summary>
    private static readonly double[][] H2Ema = [[N, 121.5], [1099.0 / 6, 3847.0 / 18], [15151.0 / 54, 50683.0 / 162]];

    private static List<FeatureSpec> H2Specs() =>
    [
        new("today_open", Timeframe.M15),
        new("prev_day_close", Timeframe.M15),
        new("vwap", Timeframe.M15),
        new("close", Timeframe.M15),
        new("ema", Timeframe.M15, PriceSource.Close, 2),
        new("close"),
    ];

    private static IEnumerable<Bar> ThirtyMinutes(DateOnly date, double baseClose) =>
        Enumerable.Range(0, 30).Select(i => Minutes.Close(date, Minutes.Open + i, baseClose + i));

    [Fact]
    public void H2_a_coarser_lane_is_not_ready_at_any_lag_until_it_closes_its_first_bar_of_the_date()
    {
        var frame = FeatureFrame.Create(H2Specs(), new FrameOptions(HistoryDepth: 4)).Value;
        var days = new[] { BarFixtures.Mon, BarFixtures.Tue, BarFixtures.Wed };

        for (var d = 0; d < 3; d++)
        {
            var b = Bases[d];
            var previousClose = d == 0 ? N : Bases[d - 1] + 29;
            for (var i = 0; i < 30; i++)
            {
                frame.Step(Minutes.Close(days[d], Minutes.Open + i, b + i));
                var label = $"day {d + 1}, minute {i}";

                // the 15m closes before today, newest first: the two bars of each earlier day (B2 then B1)
                var oldClose = new[] { d >= 1 ? Bases[d - 1] + 29 : N, d >= 1 ? Bases[d - 1] + 14 : N, d >= 2 ? Bases[d - 2] + 29 : N, d >= 2 ? Bases[d - 2] + 14 : N };

                if (i < 14)
                {
                    // 09:15 to 09:28: no 15-minute bar of the date has closed: the three restarting features are pending (the old frame showed yesterday's values here, and for day 3 the prev_day_close of day 1)
                    foreach (var h in new[] { 0, 1, 2 })
                    {
                        for (var lag = 0; lag < 4; lag++)
                        {
                            ShouldMatch(frame, h, N, lag, label + ", handle " + h);
                        }
                    }

                    // the continuous 15m features are untouched: close and ema(2) show the previous day's last bars
                    for (var lag = 0; lag < 4; lag++)
                    {
                        ShouldMatch(frame, 3, oldClose[lag], lag, label + ", close(15m)");
                    }

                    ShouldMatch(frame, 4, d == 0 ? N : H2Ema[d - 1][1], 0, label + ", ema(2) lag 0");
                    ShouldMatch(frame, 4, d == 0 ? N : H2Ema[d - 1][0], 1, label + ", ema(2) lag 1");
                }
                else if (i < 29)
                {
                    // 09:29 to 09:43: B1 has closed, B2 is forming: one bar of the date
                    var vwap = b + 28.0 / 3;
                    ShouldMatch(frame, 0, b, 0, label + ", today_open");
                    ShouldMatch(frame, 0, N, 1, label + ", today_open");
                    ShouldMatch(frame, 1, previousClose, 0, label + ", prev_day_close");
                    ShouldMatch(frame, 1, N, 1, label + ", prev_day_close");
                    ShouldMatch(frame, 2, vwap, 0, label + ", vwap");
                    ShouldMatch(frame, 2, N, 1, label + ", vwap");
                    ShouldMatch(frame, 3, b + 14, 0, label + ", close(15m)");
                    ShouldMatch(frame, 3, d == 0 ? N : Bases[d - 1] + 29, 1, label + ", close(15m)");
                    ShouldMatch(frame, 4, H2Ema[d][0], 0, label + ", ema(2) lag 0");
                    ShouldMatch(frame, 4, d == 0 ? N : H2Ema[d - 1][1], 1, label + ", ema(2) lag 1");
                }
                else
                {
                    // 09:44: B2 closes: two bars of the date
                    ShouldMatch(frame, 0, b, 0, label + ", today_open");
                    ShouldMatch(frame, 0, b, 1, label + ", today_open");
                    ShouldMatch(frame, 0, N, 2, label + ", today_open");
                    ShouldMatch(frame, 1, previousClose, 0, label + ", prev_day_close");
                    ShouldMatch(frame, 1, previousClose, 1, label + ", prev_day_close");
                    ShouldMatch(frame, 1, N, 2, label + ", prev_day_close");
                    ShouldMatch(frame, 2, b + 101.0 / 6, 0, label + ", vwap");
                    ShouldMatch(frame, 2, b + 28.0 / 3, 1, label + ", vwap");
                    ShouldMatch(frame, 2, N, 2, label + ", vwap");
                    ShouldMatch(frame, 3, b + 29, 0, label + ", close(15m)");
                    ShouldMatch(frame, 3, b + 14, 1, label + ", close(15m)");
                    ShouldMatch(frame, 4, H2Ema[d][1], 0, label + ", ema(2) lag 0");
                    ShouldMatch(frame, 4, H2Ema[d][0], 1, label + ", ema(2) lag 1");
                }
            }
        }
    }

    [Fact]
    public void H2_a_pending_lane_is_not_ready_through_the_other_read_paths_too()
    {
        var frame = FeatureFrame.Create(H2Specs(), new FrameOptions(HistoryDepth: 4)).Value;
        foreach (var bar in ThirtyMinutes(BarFixtures.Mon, 100))
        {
            frame.Step(bar);
        }

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open, 200));

        // Tuesday 09:15: the 15m lane has closed nothing of Tuesday
        frame.IsReady(0).ShouldBeFalse();
        frame[1].ShouldBeNaN();
        frame.TryGet(new FeatureSpec("vwap", Timeframe.M15), out var value).ShouldBeFalse();
        value.ShouldBeNaN();
        frame.IsReadyAt(1, 0).ShouldBeFalse();
        frame.IsReady(3).ShouldBeTrue("close(15m) is continuous");
        frame[3].ShouldBe(129.0);
        frame.WarmUp(0).ShouldBe(1, "the warm-up is a property of the feature and does not change");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
    // H3: a daily bar is a whole session; nothing restarts on 1D. Four days told by two bars (09:15 and 15:29): open, high, low, close
    //   d1 100 110  95 105 | d2 120 125 118 121 | d3 130 131 125 126 | d4 124 128 120 127
    //   gap of d2 = (120 - 105)/105·100, of d3 = (130 - 121)/121·100, of d4 = (124 - 126)/126·100
    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void H3_a_daily_feature_reads_the_days_before_by_position_and_is_never_pending()
    {
        var specs = new FeatureSpec[]
        {
            new("today_open", Timeframe.D1), new("today_high", Timeframe.D1), new("prev_day_close", Timeframe.D1), new("gap_pct", Timeframe.D1), new("close", Timeframe.D1),
        };
        var frame = FeatureFrame.Create(specs, new FrameOptions(HistoryDepth: 4)).Value;
        (DateOnly Date, double Open, double High, double Low, double Close, long Volume)[] days =
        [
            (BarFixtures.Mon, 100, 110, 95, 105, 1_000), (BarFixtures.Tue, 120, 125, 118, 121, 2_000), (BarFixtures.Wed, 130, 131, 125, 126, 3_000), (BarFixtures.Thu, 124, 128, 120, 127, 4_000),
        ];
        var gapTwo = 15.0 / 105 * 100;
        var gapThree = 9.0 / 121 * 100;
        var gapFour = -2.0 / 126 * 100;

        foreach (var (date, open, high, low, close, volume) in days.Take(2))
        {
            foreach (var bar in BarFixtures.Brief(date, open, high, low, close, volume))
            {
                frame.Step(bar);
            }
        }

        // Wednesday 09:15: the daily lane shows Tuesday's bar, with Monday's one bar back; no restart, no pending
        frame.Step(BarFixtures.Brief(BarFixtures.Wed, 130, 131, 125, 126, 3_000)[0]);
        ShouldMatch(frame, 0, 120, 0, "today_open");
        ShouldMatch(frame, 0, 100, 1, "today_open");
        ShouldMatch(frame, 3, gapTwo, 0, "gap_pct");
        ShouldMatch(frame, 3, N, 1, "gap_pct (Monday had no day before it)");
        ShouldMatch(frame, 2, 105, 0, "prev_day_close");
        ShouldMatch(frame, 2, N, 1, "prev_day_close");

        frame.Step(BarFixtures.Brief(BarFixtures.Wed, 130, 131, 125, 126, 3_000)[1]);
        foreach (var bar in BarFixtures.Brief(BarFixtures.Thu, 124, 128, 120, 127, 4_000))
        {
            frame.Step(bar);
        }

        // after Thursday's close
        double[][] expected =
        [
            [124, 130, 120, 100],
            [128, 131, 125, 110],
            [126, 121, 105, N],
            [gapFour, gapThree, gapTwo, N],
            [127, 126, 121, 105],
        ];
        for (var h = 0; h < specs.Length; h++)
        {
            for (var lag = 0; lag < 4; lag++)
            {
                ShouldMatch(frame, h, expected[h][lag], lag, specs[h].Name);
            }
        }
    }

    [Fact]
    public void H3_vwap_and_the_opening_range_on_a_day_and_every_session_feature_on_a_week_or_month_are_still_refused()
    {
        foreach (var (name, timeframe, message) in new[]
        {
            ("vwap", Timeframe.D1, "'vwap' needs intraday bars; it cannot be computed on 1D."),
            ("or_high", Timeframe.D1, "'or_high' needs intraday bars; it cannot be computed on 1D."),
            ("vwap", Timeframe.W1, "'vwap' needs intraday bars; it cannot be computed on 1W."),
            ("today_open", Timeframe.W1, "'today_open' is a value of the trading session; it cannot be computed on 1W (read it on 1m or 1D)."),
            ("prev_day_close", Timeframe.Mo1, "'prev_day_close' is a value of the trading session; it cannot be computed on 1M (read it on 1m or 1D)."),
            ("gap_pct", Timeframe.Mo1, "'gap_pct' is a value of the trading session; it cannot be computed on 1M (read it on 1m or 1D)."),
        })
        {
            var spec = IndicatorCatalog.TryGet(name, out var info) ? info.DefaultSpec(timeframe) : throw new InvalidOperationException(name);

            var result = FeatureFrame.Create([spec]);

            result.IsFailure.ShouldBeTrue(name + " on " + timeframe.ToCode());
            var field = result.Error.Fields.ShouldNotBeNull().Single();
            field.Field.ShouldBe("specs[0].timeframe");
            field.Message.ShouldBe(message);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
    // H4: a bucket that closes late. Monday has no 15:29 minute: the 15:15 bucket (15:15 to 15:28, closes 110) cannot close at 15:29 and is closed by the first bar of the next date.
    //   Monday: 15:00 to 15:14 close 100, 15:15 to 15:28 close 110. The 15m bars: B(15:00) open 100 high 100 low 100 close 100; B(15:15) open 110, close 110.
    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static List<Bar> MondayWithoutTheLastMinute()
    {
        var bars = new List<Bar>();
        for (var m = Minutes.At(15, 0); m < Minutes.At(15, 29); m++)
        {
            bars.Add(Minutes.Close(BarFixtures.Mon, m, m < Minutes.At(15, 15) ? 100 : 110));
        }

        return bars;
    }

    private static List<FeatureSpec> LateSpecs() =>
    [
        new("vwap", Timeframe.M15),
        new("today_open", Timeframe.M15),
        new("prev_day_close", Timeframe.M15),
        new("close", Timeframe.M15),
        new("vwap"),
    ];

    [Fact]
    public void H4_a_late_closed_bucket_of_the_old_date_is_consumed_with_the_old_date_and_leaves_the_new_date_pending()
    {
        var frame = FeatureFrame.Create(LateSpecs(), new FrameOptions(HistoryDepth: 4)).Value;
        foreach (var bar in MondayWithoutTheLastMinute())
        {
            frame.Step(bar);
        }

        // Monday's last 15m bar that closed on time is the 15:00 one; the 15:15 bucket is still open
        ShouldMatch(frame, 3, 100, 0, "close(15m) before the late close");
        frame.BarClosed(Timeframe.M15).ShouldBeFalse();

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open, 120));

        frame.BarClosed(Timeframe.M15).ShouldBeTrue("the first bar of Tuesday closes Monday's 15:15 bucket");
        frame.TryLastClosed(Timeframe.M15, out var closed).ShouldBeTrue();
        (closed.Complete, closed.Bar.Close).ShouldBe((false, 110.0));
        ShouldMatch(frame, 3, 110, 0, "close(15m) is continuous: Monday's late bar");
        ShouldMatch(frame, 3, 100, 1, "close(15m)");
        for (var h = 0; h < 3; h++)
        {
            for (var lag = 0; lag < 4; lag++)
            {
                ShouldMatch(frame, h, N, lag, "the 15m session features of Tuesday are pending, handle " + h);
            }
        }

        ShouldMatch(frame, 4, 120, 0, "the 1m lane is in Tuesday at once");
        ShouldMatch(frame, 4, N, 1, "the 1m vwap one bar back is not Monday's");
    }

    [Fact]
    public void H4_after_a_late_close_the_first_bucket_of_the_new_date_has_a_history_of_its_own()
    {
        var frame = FeatureFrame.Create(LateSpecs(), new FrameOptions(HistoryDepth: 4)).Value;
        foreach (var bar in MondayWithoutTheLastMinute())
        {
            frame.Step(bar);
        }

        // Tuesday 09:15 to 09:29 at 120: the 15m bar (open 120, high 120, low 120, close 120) closes at 09:29
        for (var m = Minutes.Open; m < Minutes.Open + 15; m++)
        {
            frame.Step(Minutes.Close(BarFixtures.Tue, m, 120));
        }

        ShouldMatch(frame, 0, 120, 0, "vwap(15m)");
        ShouldMatch(frame, 0, N, 1, "vwap(15m): Monday's two bars are not read");
        ShouldMatch(frame, 1, 120, 0, "today_open(15m)");
        ShouldMatch(frame, 1, N, 1, "today_open(15m)");
        ShouldMatch(frame, 2, 110, 0, "prev_day_close(15m): the last bar of Monday is the late 15:15 bucket (110)");
        ShouldMatch(frame, 2, N, 1, "prev_day_close(15m)");
        ShouldMatch(frame, 3, 120, 0, "close(15m)");
        ShouldMatch(frame, 3, 110, 1, "close(15m): by position, the late bucket");
        ShouldMatch(frame, 3, 100, 2, "close(15m)");
    }

    [Fact]
    public void H4_a_minute_that_closes_the_late_bucket_and_the_new_dates_own_bucket_leaves_a_one_bar_session()
    {
        // Tuesday's first bar is the 09:29 minute (09:15 to 09:28 are missing): it closes Monday's late bucket and, being the last minute of its own 09:15 bucket, Tuesday's bucket too
        var frame = FeatureFrame.Create(LateSpecs(), new FrameOptions(HistoryDepth: 4)).Value;
        foreach (var bar in MondayWithoutTheLastMinute())
        {
            frame.Step(bar);
        }

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + 14, 120));

        frame.ClosedBarsAt(Timeframe.M15).ShouldBe(2);
        ShouldMatch(frame, 0, 120, 0, "vwap(15m): Tuesday's single bar");
        ShouldMatch(frame, 0, N, 1, "vwap(15m): the late bucket of Monday belongs to Monday");
        ShouldMatch(frame, 1, 120, 0, "today_open(15m)");
        ShouldMatch(frame, 1, N, 1, "today_open(15m)");
        ShouldMatch(frame, 2, 110, 0, "prev_day_close(15m)");
        ShouldMatch(frame, 2, N, 1, "prev_day_close(15m)");
        ShouldMatch(frame, 3, 120, 0, "close(15m)");
        ShouldMatch(frame, 3, 110, 1, "close(15m) is by position: Monday's late bucket");
        ShouldMatch(frame, 3, 100, 2, "close(15m)");
        ShouldMatch(frame, 4, 120, 0, "vwap(1m)");
        ShouldMatch(frame, 4, N, 1, "vwap(1m)");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
    // H5: an opening range of several bars. Two sessions of 45 flat one-minute bars (closes base + i, base 100 and 200): three 15-minute bars a day, B1 = minutes 0-14 (high base+14, low base), B2 = 15-29 (high base+29,
    // low base+15), B3 = 30-44. or_high(30) / or_low(30) on 15m is the range of B1 and B2: base+29 / base, known when B2 has closed (minute 29); a lag across the open is unknown for the first bars of a session on top of that.
    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void H5_the_opening_range_of_several_bars_is_unknown_until_the_range_has_closed_and_one_bar_back_for_one_more_bar()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("or_high", Timeframe.M15, PriceSource.Close, 30), new FeatureSpec("or_low", Timeframe.M15, PriceSource.Close, 30), new FeatureSpec("close", Timeframe.M15)], new FrameOptions(HistoryDepth: 4)).Value;
        var days = new[] { BarFixtures.Mon, BarFixtures.Tue };

        for (var d = 0; d < 2; d++)
        {
            var b = Bases[d];
            for (var i = 0; i < 45; i++)
            {
                frame.Step(Minutes.Close(days[d], Minutes.Open + i, b + i));
                var label = $"day {d + 1}, minute {i}";
                if (i < 29)
                {
                    // the range has not closed (B2 closes at minute 29): lag 0 is unknown; one bar back is unknown for the session's first bars too; before B1 closes nothing of the date is closed (pending on day 2)
                    for (var lag = 0; lag < 4; lag++)
                    {
                        ShouldMatch(frame, 0, N, lag, label + ", or_high(30)");
                        ShouldMatch(frame, 1, N, lag, label + ", or_low(30)");
                    }
                }
                else if (i < 44)
                {
                    // B2 has closed, B3 is forming: the range is known, the bar before it (B1) did not know it
                    ShouldMatch(frame, 0, b + 29, 0, label + ", or_high(30)");
                    ShouldMatch(frame, 0, N, 1, label + ", or_high(30)");
                    ShouldMatch(frame, 1, b, 0, label + ", or_low(30)");
                    ShouldMatch(frame, 1, N, 1, label + ", or_low(30)");
                }
                else
                {
                    // B3 has closed: the range stays, one bar back is the range after B2, two bars back (after B1) is unknown
                    ShouldMatch(frame, 0, b + 29, 0, label + ", or_high(30)");
                    ShouldMatch(frame, 0, b + 29, 1, label + ", or_high(30)");
                    ShouldMatch(frame, 0, N, 2, label + ", or_high(30)");
                    ShouldMatch(frame, 1, b, 1, label + ", or_low(30)");
                    ShouldMatch(frame, 1, N, 2, label + ", or_low(30)");
                }
            }
        }
    }

    [Fact]
    public void H6_an_opening_range_of_one_bar_is_known_from_the_first_bar_of_the_session_and_has_no_history_before_it()
    {
        // or_high(1)/or_low(1) on 1m are the high and the low of the first bar of the session. Flat bars (high = low = close): Monday 100, 98; Tuesday 122, 121.
        var frame = FeatureFrame.Create([new FeatureSpec("or_high", Timeframe.M1, PriceSource.Close, 1), new FeatureSpec("or_low", Timeframe.M1, PriceSource.Close, 1)], new FrameOptions(HistoryDepth: 4)).Value;
        var bars = new[] { Minutes.Close(BarFixtures.Mon, Minutes.Open, 100), Minutes.Close(BarFixtures.Mon, Minutes.Open + 1, 98), Minutes.Close(BarFixtures.Tue, Minutes.Open, 122), Minutes.Close(BarFixtures.Tue, Minutes.Open + 1, 121) };
        double[][] high = [[100, N, N, N], [100, 100, N, N], [122, N, N, N], [122, 122, N, N]];
        double[][] low = [[100, N, N, N], [100, 100, N, N], [122, N, N, N], [122, 122, N, N]];

        for (var step = 0; step < bars.Length; step++)
        {
            frame.Step(bars[step]);
            for (var lag = 0; lag < 4; lag++)
            {
                ShouldMatch(frame, 0, high[step][lag], lag, $"step {step + 1}, or_high(1)");
                ShouldMatch(frame, 1, low[step][lag], lag, $"step {step + 1}, or_low(1)");
            }
        }
    }

    [Fact]
    public void H6_an_opening_range_of_one_bar_of_a_coarser_lane_is_pending_until_its_first_bar_of_the_date()
    {
        // or_high(15)/or_low(15) on 15m are the high and the low of the first 15-minute bar: Monday 100 to 114, Tuesday (closes 200 + i) 200 to 214
        var frame = FeatureFrame.Create([new FeatureSpec("or_high", Timeframe.M15, PriceSource.Close, 15), new FeatureSpec("or_low", Timeframe.M15, PriceSource.Close, 15)], new FrameOptions(HistoryDepth: 4)).Value;
        var days = new[] { BarFixtures.Mon, BarFixtures.Tue };

        for (var d = 0; d < 2; d++)
        {
            for (var i = 0; i < 30; i++)
            {
                frame.Step(Minutes.Close(days[d], Minutes.Open + i, Bases[d] + i));
                var label = $"day {d + 1}, minute {i}";
                if (i < 14)
                {
                    // no 15-minute bar of the date yet: the old frame showed Monday's 114 and 100 on Tuesday
                    ShouldMatch(frame, 0, N, 0, label + ", or_high(15)");
                    ShouldMatch(frame, 0, N, 1, label + ", or_high(15)");
                    ShouldMatch(frame, 1, N, 0, label + ", or_low(15)");
                    ShouldMatch(frame, 1, N, 1, label + ", or_low(15)");
                }
                else
                {
                    ShouldMatch(frame, 0, Bases[d] + 14, 0, label + ", or_high(15)");
                    ShouldMatch(frame, 1, Bases[d], 0, label + ", or_low(15)");
                    ShouldMatch(frame, 0, i == 29 ? Bases[d] + 14 : N, 1, label + ", or_high(15): one bar back exists once the second bar has closed");
                    ShouldMatch(frame, 1, i == 29 ? Bases[d] : N, 1, label + ", or_low(15)");
                }
            }
        }
    }

    [Fact]
    public void A_holiday_between_two_sessions_is_no_session_the_session_after_it_starts_afresh_and_reads_the_one_before_the_holiday_as_the_previous_one()
    {
        // Monday, no Tuesday (a weekday holiday), Wednesday: one restart at Wednesday's first bar, prev_day_close is Monday's last close
        var frame = EdgeFrame();
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open, 100));
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 1, 98));

        frame.Step(Minutes.Close(BarFixtures.Wed, Minutes.Open, 120));

        ShouldMatch(frame, 0, 120, 0, "vwap");
        ShouldMatch(frame, 0, N, 1, "vwap");
        ShouldMatch(frame, 1, 120, 0, "today_open");
        ShouldMatch(frame, 3, 98, 0, "prev_day_close is the last close before the holiday");
        ShouldMatch(frame, 3, N, 1, "prev_day_close");
        ShouldMatch(frame, 2, 99 + 2.0 / 3 * (120 - 99), 0, "ema(2) runs on across the holiday");
        ShouldMatch(frame, 2, 99, 1, "ema(2)");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
    // Edge sessions. Each frame has vwap, today_open and ema(2) next to prev_day_close; flat bars of volume 10.
    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static FeatureFrame EdgeFrame() =>
        FeatureFrame.Create(
            [new FeatureSpec("vwap"), new FeatureSpec("today_open"), new FeatureSpec("ema", Timeframe.M1, PriceSource.Close, 2), new FeatureSpec("prev_day_close")],
            new FrameOptions(HistoryDepth: 4)).Value;

    [Fact]
    public void A_session_whose_opening_minutes_are_missing_starts_at_its_first_bar()
    {
        var frame = EdgeFrame();
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open, 100));
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 1, 98));

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.At(9, 30), 120));

        // ema(2): 99 after Monday's two bars, then 99 + 2/3 · (120 − 99) = 113
        ShouldMatch(frame, 0, 120, 0, "vwap at 09:30");
        ShouldMatch(frame, 0, N, 1, "vwap: the session starts at the first bar present");
        ShouldMatch(frame, 1, 120, 0, "today_open");
        ShouldMatch(frame, 1, N, 1, "today_open");
        ShouldMatch(frame, 2, 113, 0, "ema(2)");
        ShouldMatch(frame, 2, 99, 1, "ema(2)");
        ShouldMatch(frame, 3, 98, 0, "prev_day_close");

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.At(9, 31), 118));
        ShouldMatch(frame, 0, 119, 0, "vwap");
        ShouldMatch(frame, 0, 120, 1, "vwap");
        ShouldMatch(frame, 0, N, 2, "vwap");
        ShouldMatch(frame, 2, 113 + 2.0 / 3 * (118 - 113), 0, "ema(2)");
        ShouldMatch(frame, 2, 113, 1, "ema(2)");
    }

    [Fact]
    public void Friday_then_monday_is_one_restart_at_the_first_bar_of_monday()
    {
        var frame = EdgeFrame();
        frame.Step(Minutes.Close(BarFixtures.Fri, Minutes.Open, 100));
        frame.Step(Minutes.Close(BarFixtures.Fri, Minutes.Open + 1, 98));

        frame.Step(Minutes.Close(BarFixtures.Fri.AddDays(3), Minutes.Open, 120));

        ShouldMatch(frame, 0, 120, 0, "vwap");
        ShouldMatch(frame, 0, N, 1, "vwap");
        ShouldMatch(frame, 3, 98, 0, "prev_day_close is Friday's last close");
        ShouldMatch(frame, 3, N, 1, "prev_day_close");

        frame.Step(Minutes.Close(BarFixtures.Fri.AddDays(3), Minutes.Open + 1, 118));
        ShouldMatch(frame, 0, 119, 0, "vwap");
        ShouldMatch(frame, 0, 120, 1, "vwap: no second restart at the second bar");
        ShouldMatch(frame, 3, 98, 1, "prev_day_close");
    }

    [Fact]
    public void A_saturday_session_is_a_session_of_its_own_and_the_monday_after_it_reads_the_saturday_as_the_previous_one()
    {
        var frame = EdgeFrame();
        frame.Step(Minutes.Close(BarFixtures.Fri, Minutes.Open, 100));
        frame.Step(Minutes.Close(BarFixtures.Sat, Minutes.Open, 130));

        ShouldMatch(frame, 0, 130, 0, "vwap at the Saturday session's first bar");
        ShouldMatch(frame, 0, N, 1, "vwap");
        ShouldMatch(frame, 3, 100, 0, "prev_day_close: Friday's close");
        ShouldMatch(frame, 3, N, 1, "prev_day_close");

        frame.Step(Minutes.Close(BarFixtures.Sat, Minutes.Open + 1, 132));
        frame.Step(Minutes.Close(BarFixtures.Sat.AddDays(2), Minutes.Open, 120));

        ShouldMatch(frame, 0, 120, 0, "vwap on Monday");
        ShouldMatch(frame, 0, N, 1, "vwap");
        ShouldMatch(frame, 3, 132, 0, "prev_day_close: the Saturday session's last close");
        ShouldMatch(frame, 3, N, 1, "prev_day_close");
    }

    [Fact]
    public void A_muhurat_evening_on_a_date_of_its_own_restarts_at_18_15_and_so_does_the_monday_after_it()
    {
        var frame = EdgeFrame();
        frame.Step(Minutes.Close(BarFixtures.Fri, Minutes.Open, 100));
        frame.Step(Minutes.Close(BarFixtures.Sun, Minutes.At(18, 15), 90));

        ShouldMatch(frame, 0, 90, 0, "vwap at 18:15");
        ShouldMatch(frame, 0, N, 1, "vwap");
        ShouldMatch(frame, 3, 100, 0, "prev_day_close");

        frame.Step(Minutes.Close(BarFixtures.Sun, Minutes.At(18, 16), 94));
        ShouldMatch(frame, 0, 92, 0, "vwap");
        ShouldMatch(frame, 0, 90, 1, "vwap");

        frame.Step(Minutes.Close(BarFixtures.Sun.AddDays(1), Minutes.Open, 120));
        ShouldMatch(frame, 0, 120, 0, "vwap on Monday");
        ShouldMatch(frame, 0, N, 1, "vwap");
        ShouldMatch(frame, 3, 94, 0, "prev_day_close: the evening's last close");
    }

    [Fact]
    public void An_evening_block_on_a_date_that_also_has_a_regular_block_is_the_same_session_and_does_not_restart_the_history()
    {
        // documented limit: the session is the India date, exactly as the indicators already treat it
        var frame = EdgeFrame();
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open, 100));
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 1, 98));

        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.At(18, 15), 90));

        ShouldMatch(frame, 0, 96, 0, "vwap: (100 + 98 + 90) / 3, the running mean goes on");
        ShouldMatch(frame, 0, 99, 1, "vwap: the history goes on too");
        ShouldMatch(frame, 0, 100, 2, "vwap");
        ShouldMatch(frame, 1, 100, 1, "today_open");
    }

    [Fact]
    public void A_repeated_first_bar_of_a_date_is_counted_and_restarts_nothing()
    {
        var clean = EdgeFrame();
        var dirty = EdgeFrame();
        var series = new[]
        {
            Minutes.Close(BarFixtures.Mon, Minutes.Open, 100), Minutes.Close(BarFixtures.Mon, Minutes.Open + 1, 98),
            Minutes.Close(BarFixtures.Tue, Minutes.Open, 120), Minutes.Close(BarFixtures.Tue, Minutes.Open + 1, 118), Minutes.Close(BarFixtures.Tue, Minutes.Open + 2, 125),
        };

        foreach (var bar in series.Take(3))
        {
            clean.Step(bar);
            dirty.Step(bar);
        }

        // the first bar of Tuesday again (other price), then an older bar of Monday: both skipped
        dirty.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open, 150));
        dirty.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 1, 150));
        foreach (var bar in series.Skip(3))
        {
            clean.Step(bar);
            dirty.Step(bar);
        }

        dirty.IgnoredBars.ShouldBe(2);
        for (var h = 0; h < 4; h++)
        {
            for (var lag = 0; lag < 4; lag++)
            {
                Tolerance.Identical(clean.At(h, lag), dirty.At(h, lag)).ShouldBeTrue($"handle {h}, lag {lag}");
            }
        }

        // by hand: after Tuesday's third bar vwap = (120 + 118 + 125)/3 = 121, one back (120 + 118)/2 = 119, two back 120, three back none
        ShouldMatch(dirty, 0, 121, 0, "vwap");
        ShouldMatch(dirty, 0, 119, 1, "vwap");
        ShouldMatch(dirty, 0, 120, 2, "vwap");
        ShouldMatch(dirty, 0, N, 3, "vwap");
    }

    [Fact]
    public void After_a_reset_the_frame_replays_exactly_like_a_fresh_one_also_when_the_reset_came_while_a_lane_was_pending()
    {
        var specs = H2Specs();
        var reused = FeatureFrame.Create(specs, new FrameOptions(HistoryDepth: 4)).Value;
        var fresh = FeatureFrame.Create(specs, new FrameOptions(HistoryDepth: 4)).Value;
        var series = new List<Bar>();
        series.AddRange(ThirtyMinutes(BarFixtures.Mon, 100));
        series.AddRange(ThirtyMinutes(BarFixtures.Tue, 200));

        // run into Tuesday's pending stretch (09:15 to 09:28), then reset
        foreach (var bar in series.Take(30 + 6))
        {
            reused.Step(bar);
        }

        reused.Reset();

        for (var i = 0; i < series.Count; i++)
        {
            reused.Step(series[i]);
            fresh.Step(series[i]);
            for (var h = 0; h < specs.Count; h++)
            {
                for (var lag = 0; lag < 4; lag++)
                {
                    if (!Tolerance.Identical(reused.At(h, lag), fresh.At(h, lag)))
                    {
                        Assert.Fail($"step {i}, handle {h}, lag {lag}: the reset frame has {reused.At(h, lag):R}, a fresh one {fresh.At(h, lag):R}");
                    }
                }

                reused.IsReady(h).ShouldBe(fresh.IsReady(h));
            }
        }
    }

    [Fact]
    public void A_frame_fed_from_the_middle_of_a_day_takes_the_first_bar_it_is_given_as_the_start_of_its_session()
    {
        var frame = EdgeFrame();
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.At(11, 0), 100));
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.At(11, 1), 98));

        ShouldMatch(frame, 0, 99, 0, "vwap");
        ShouldMatch(frame, 0, 100, 1, "vwap");
        ShouldMatch(frame, 0, N, 2, "vwap");
    }
}
