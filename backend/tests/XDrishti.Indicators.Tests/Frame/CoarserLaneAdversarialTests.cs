using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Frame;

/// <summary>
/// ISS-13, adversarial, coarser lanes: a restarting feature on 5m, 10m, 15m, 30m or 1h is pending (NaN, not ready, at every lag) from the first minute of a new date until its lane closes its first bar of the date, and
/// from then on shows the session's own bars only. The sessions are made of whole buckets of flat bars (every minute of a bucket at one price, volume 10 per minute), so each expected number follows from the list of
/// bucket prices alone: with S buckets closed, <c>today_open</c> is the first price, <c>today_high</c>/<c>today_low</c> the extremes of the first S, <c>vwap</c> their mean (equal volumes), <c>or_*(one bar)</c> the first
/// price, and <c>prev_day_*</c> come from the buckets of the session before. The expectation is a loop over the price lists, not read from the frame.
/// </summary>
public sealed class CoarserLaneAdversarialTests
{
    private static readonly double Nan = double.NaN;

    /// <summary>Bucket prices of three sessions: Monday, Tuesday, Wednesday (Wednesday is a single bucket).</summary>
    private static readonly double[][] Prices = [[100, 110, 130], [120, 90, 140], [150]];

    private static readonly DateOnly[] Dates = [BarFixtures.Mon, BarFixtures.Tue, BarFixtures.Wed];

    public static TheoryData<Timeframe> Coarser => new(Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1);

    private static List<Bar> SessionsOf(int bucketMinutes)
    {
        var bars = new List<Bar>();
        for (var s = 0; s < Prices.Length; s++)
        {
            for (var b = 0; b < Prices[s].Length; b++)
            {
                for (var i = 0; i < bucketMinutes; i++)
                {
                    bars.Add(Minutes.Close(Dates[s], Minutes.Open + b * bucketMinutes + i, Prices[s][b]));
                }
            }
        }

        return bars;
    }

    /// <summary>The value of the named restarting feature after S closed buckets of session s (S at least 1), worked out from the price list.</summary>
    private static double Value(string name, int s, int closed, int bucketMinutes)
    {
        var today = Prices[s].Take(closed).ToArray();
        var previous = s > 0 ? Prices[s - 1] : null;
        return name switch
        {
            "vwap" => today.Average(),
            "or_high" or "or_low" or "today_open" => Prices[s][0],
            "today_high" => today.Max(),
            "today_low" => today.Min(),
            "prev_day_open" => previous is null ? Nan : previous[0],
            "prev_day_high" => previous is null ? Nan : previous.Max(),
            "prev_day_low" => previous is null ? Nan : previous.Min(),
            "prev_day_close" => previous is null ? Nan : previous[^1],
            "prev_day_volume" => previous is null ? Nan : previous.Length * bucketMinutes * 10.0,
            "gap_pct" => previous is null ? Nan : (Prices[s][0] - previous[^1]) / previous[^1] * 100,
            _ => throw new ArgumentException(name),
        };
    }

    [Theory]
    [MemberData(nameof(Coarser))]
    public void On_a_coarser_lane_a_session_feature_is_pending_until_the_first_bucket_of_the_date_closes_and_then_holds_that_session_only(Timeframe timeframe)
    {
        var m = timeframe.Minutes();
        var specs = new List<FeatureSpec>(AdversarialFrames.TwelveOn(timeframe)) { AdversarialFrames.Spec("ema", timeframe, 2), AdversarialFrames.Spec("close", timeframe), AdversarialFrames.Spec("vwap", Timeframe.M1) };
        var frame = AdversarialFrames.Create(specs, 4);
        var ema = specs.Count - 3;
        var rawClose = specs.Count - 2;
        var oneMinuteVwap = specs.Count - 1;
        var bars = SessionsOf(m);

        // the continuous reference: every bucket close in order, ema(2) seeded with the mean of the first two
        var closes = Prices.SelectMany(p => p).ToArray();
        var emas = new double[closes.Length];
        for (var i = 1; i < closes.Length; i++)
        {
            emas[i] = i == 1 ? (closes[0] + closes[1]) / 2 : emas[i - 1] + 2.0 / 3 * (closes[i] - emas[i - 1]);
        }

        emas[0] = Nan;

        var n = 0;
        var closedBefore = 0;
        for (var s = 0; s < Prices.Length; s++)
        {
            for (var j = 0; j < Prices[s].Length * m; j++, n++)
            {
                frame.Step(bars[n]);

                var closed = (j + 1) / m;
                var totalClosed = closedBefore + closed;
                var where = $"{timeframe.ToCode()} session {s} minute {j} ({closed} bucket(s) closed)";
                foreach (var name in AdversarialFrames.Twelve)
                {
                    var handle = specs.FindIndex(x => x.Name == name);
                    for (var k = 0; k < 4; k++)
                    {
                        var expected = closed >= 1 && k < closed ? Value(name, s, closed - k, m) : Nan;
                        frame.At(handle, k).ShouldBeNear(expected, $"{name} lag {k} at {where}");
                        (k == 0 ? frame.IsReady(handle) : frame.IsReadyAt(handle, k)).ShouldBe(!double.IsNaN(expected), $"{name} readiness lag {k} at {where}");
                    }
                }

                for (var k = 0; k < 4; k++)
                {
                    var index = totalClosed - 1 - k;
                    frame.At(ema, k).ShouldBeNear(index >= 0 ? emas[index] : Nan, $"ema(2) lag {k} at {where}: rolling features run across the sessions");
                    frame.At(rawClose, k).ShouldBeNear(index >= 0 ? closes[index] : Nan, $"raw close lag {k} at {where}");
                }

                // the 1-minute lane next to them is in its own session from the first minute: vwap of the minutes so far (one price per bucket, equal volumes)
                var minuteVwap = Enumerable.Range(0, j + 1).Select(x => Prices[s][x / m]).Average();
                frame.At(oneMinuteVwap, 0).ShouldBeNear(minuteVwap, $"1m vwap at {where}");
                frame.At(oneMinuteVwap, 1).ShouldBeNear(j >= 1 ? Enumerable.Range(0, j).Select(x => Prices[s][x / m]).Average() : Nan, $"1m vwap lag 1 at {where}");
            }

            closedBefore += Prices[s].Length;
        }
    }

    [Fact]
    public void A_lane_that_never_closes_a_bar_of_a_date_stays_pending_for_the_whole_date_and_its_late_bucket_is_taken_with_the_next_one()
    {
        // Monday is a regular full session (flat at 100 then 110 for the last hour). Tuesday has only three minutes, 15:20-15:22, at price 130: the 1h bucket 15:15-15:29 starts but cannot close in the session
        // (no 15:29 minute), so the 1h lane takes no bar of Tuesday on Tuesday; Wednesday's first minute closes it (a late bucket, taken with Tuesday's date) and Wednesday opens a new session on the lane.
        var bars = new List<Bar>();
        bars.AddRange(Enumerable.Range(0, 375).Select(i => Minutes.Close(BarFixtures.Mon, Minutes.Open + i, i < 300 ? 100 : 110)));
        bars.AddRange(Enumerable.Range(0, 3).Select(i => Minutes.Close(BarFixtures.Tue, Minutes.At(15, 20) + i, 130)));
        bars.AddRange(Enumerable.Range(0, 60).Select(i => Minutes.Close(BarFixtures.Wed, Minutes.Open + i, 150)));
        var specs = new List<FeatureSpec>
        {
            AdversarialFrames.Spec("today_open", Timeframe.H1), AdversarialFrames.Spec("vwap", Timeframe.H1), AdversarialFrames.Spec("prev_day_close", Timeframe.H1),
            AdversarialFrames.Spec("ema", Timeframe.H1, 2), AdversarialFrames.Spec("today_open", Timeframe.M1),
        };
        var frame = AdversarialFrames.Create(specs, 4);

        // Monday ends with 7 closed hours: closes 100 x5 (09:15 .. 14:14 buckets starts 09:15, 10:15, 11:15, 12:15, 13:15 are minutes 0-299 -> five buckets at 100), 14:15 bucket (minutes 300-359) at 110, 15:15 bucket at 110.
        foreach (var bar in bars.Take(375))
        {
            frame.Step(bar);
        }

        frame.ClosedBarsAt(Timeframe.H1).ShouldBe(1, "the 15:29 minute closes the partial last hour");
        var emaAtMonday = Enumerable.Range(0, 4).Select(k => frame.At(3, k)).ToArray();

        // Tuesday: three steps; the 1h lane takes nothing; everything restarting on it is NaN at every lag, the ema is exactly where Monday left it, the 1-minute today_open is Tuesday's open from the first minute
        for (var i = 0; i < 3; i++)
        {
            frame.Step(bars[375 + i]);

            frame.BarClosed(Timeframe.H1).ShouldBeFalse();
            for (var h = 0; h < 3; h++)
            {
                for (var k = 0; k < 4; k++)
                {
                    frame.At(h, k).ShouldBeNear(Nan, $"{frame.SpecOf(h).Name} (1h) lag {k} at Tuesday minute {i}: the lane has no bar of the date");
                }
            }

            AdversarialFrames.Lags(frame, 3).ShouldBeLags(emaAtMonday, "ema(2) on 1h is untouched on Tuesday");
            frame.At(4, 0).ShouldBeNear(130, "1m today_open on Tuesday");
            frame.At(4, 1).ShouldBeNear(i >= 1 ? 130 : Nan, "1m today_open lag 1 on Tuesday");
        }

        // Wednesday 09:15: the minute closes Tuesday's late hour; the lane is pending on Wednesday (the closed bar belongs to Tuesday); the ema has taken the bar (its lag 1 is Monday's last value)
        frame.Step(bars[378]);

        frame.BarClosed(Timeframe.H1).ShouldBeTrue();
        frame.At(3, 1).ShouldBeNear(emaAtMonday[0], "the late bucket of Tuesday is one more bar of the rolling ema");
        for (var h = 0; h < 3; h++)
        {
            frame.At(h, 0).ShouldBeNear(Nan, $"{frame.SpecOf(h).Name} (1h) is pending on Wednesday 09:15");
            frame.At(h, 1).ShouldBeNear(Nan, $"{frame.SpecOf(h).Name} (1h) lag 1 is pending on Wednesday 09:15");
        }

        // Wednesday 10:14 closes Wednesday's first hour (price 150 for 60 minutes): the lane is in its Wednesday session. prev_day_close is Tuesday's late bucket (close 130), today_open 150, vwap 150, nothing at lag 1
        for (var i = 379; i < bars.Count; i++)
        {
            frame.Step(bars[i]);
        }

        frame.BarClosed(Timeframe.H1).ShouldBeTrue();
        frame.At(0, 0).ShouldBeNear(150, "today_open (1h) after Wednesday's first hour");
        frame.At(1, 0).ShouldBeNear(150, "vwap (1h) after Wednesday's first hour");
        frame.At(2, 0).ShouldBeNear(130, "prev_day_close (1h): Tuesday's only bucket");
        frame.At(0, 1).ShouldBeNear(Nan, "today_open (1h) lag 1: the lane has one bar of the date");
        frame.At(2, 1).ShouldBeNear(Nan, "prev_day_close (1h) lag 1");
    }

    /// <summary>Monday 14:45-15:28 (the 15:29 minute is missing) at 100 / 110 / 130 per 15-minute bucket, 15 minutes each except the last of 14: its 15m bucket 15:15 closes only at the first minute of the next date.</summary>
    private static List<Bar> MondayWithoutTheLastMinute() =>
    [
        .. Enumerable.Range(0, 15).Select(i => Minutes.Close(BarFixtures.Mon, Minutes.At(14, 45) + i, 100)),
        .. Enumerable.Range(0, 15).Select(i => Minutes.Close(BarFixtures.Mon, Minutes.At(15, 0) + i, 110)),
        .. Enumerable.Range(0, 14).Select(i => Minutes.Close(BarFixtures.Mon, Minutes.At(15, 15) + i, 130)),
    ];

    private static List<FeatureSpec> LateBucketSpecs() =>
    [
        AdversarialFrames.Spec("today_open", Timeframe.M15), AdversarialFrames.Spec("vwap", Timeframe.M15), AdversarialFrames.Spec("prev_day_close", Timeframe.M15), AdversarialFrames.Spec("prev_day_open", Timeframe.M15),
        AdversarialFrames.Spec("ema", Timeframe.M15, 2), AdversarialFrames.Spec("close", Timeframe.M15),
    ];

    [Fact]
    public void A_late_closed_bucket_is_taken_with_the_old_date_and_leaves_the_lane_pending_on_the_new_one()
    {
        // Monday's 15m buckets close at 100 (14:59), 110 (15:14) and 130 (late: closed by Tuesday 09:15). ema(2): 105 after the second, 105 + 2/3 (130 - 105) = 121.6667 after the third.
        var frame = AdversarialFrames.Create(LateBucketSpecs(), 4);
        foreach (var bar in MondayWithoutTheLastMinute())
        {
            frame.Step(bar);
        }

        frame.ClosedBarsAt(Timeframe.M15).ShouldBe(0);
        frame.At(4, 0).ShouldBeNear(105, "ema(2) before the late bucket");

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open, 120));

        frame.ClosedBarsAt(Timeframe.M15).ShouldBe(1, "Tuesday's first minute closes Monday's late bucket");
        frame.At(5, 0).ShouldBeNear(130, "the raw close of the 15m lane is the late bucket's close, defined");
        frame.At(5, 1).ShouldBeNear(110, "raw close lag 1");
        frame.ShouldHaveLags(4, [105 + 2.0 / 3 * (130 - 105), 105, Nan, Nan], "ema(2) takes the late bucket as one more bar");
        for (var h = 0; h < 4; h++)
        {
            for (var k = 0; k < 4; k++)
            {
                frame.At(h, k).ShouldBeNear(Nan, $"{frame.SpecOf(h).Name} (15m) lag {k}: the closed bar is Monday's, the lane has no bar of Tuesday");
            }

            frame.IsReady(h).ShouldBeFalse($"{frame.SpecOf(h).Name} is pending on Tuesday");
        }

        // Tuesday 09:16 - 09:28: still pending, nothing closes
        for (var minute = 1; minute < 14; minute++)
        {
            frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + minute, 120));

            frame.BarClosed(Timeframe.M15).ShouldBeFalse();
            frame.IsReady(0).ShouldBeFalse($"today_open (15m) pending at Tuesday minute {minute}");
            frame.IsReady(2).ShouldBeFalse($"prev_day_close (15m) pending at Tuesday minute {minute}");
        }

        // 09:29 closes Tuesday's first 15m bucket: today_open 120, prev_day_close 130 (the late bucket was Monday's last), prev_day_open 100, no history of the date
        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + 14, 120));

        frame.ClosedBarsAt(Timeframe.M15).ShouldBe(1);
        frame.ShouldHaveLags(0, [120, Nan, Nan, Nan], "today_open (15m) after Tuesday's first bucket");
        frame.ShouldHaveLags(1, [120, Nan, Nan, Nan], "vwap (15m)");
        frame.ShouldHaveLags(2, [130, Nan, Nan, Nan], "prev_day_close (15m) is the late bucket's close");
        frame.ShouldHaveLags(3, [100, Nan, Nan, Nan], "prev_day_open (15m) is the first bucket of Monday");
        var ema = 105 + 2.0 / 3 * (130 - 105);
        frame.ShouldHaveLags(4, [ema + 2.0 / 3 * (120 - ema), ema, 105, Nan], "ema(2) continues across the sessions");
    }

    [Fact]
    public void A_minute_that_closes_the_late_bucket_and_its_own_bucket_in_one_step_leaves_the_new_date_with_one_bar_and_no_history()
    {
        // Tuesday's first minute is 09:29, the last minute of the 09:15 bucket: the step closes Monday's late bucket (130) and Tuesday's own bucket (price 120, one minute) — two closes, the old one first.
        var frame = AdversarialFrames.Create(LateBucketSpecs(), 4);
        foreach (var bar in MondayWithoutTheLastMinute())
        {
            frame.Step(bar);
        }

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + 14, 120));

        frame.ClosedBarsAt(Timeframe.M15).ShouldBe(2);
        frame.ShouldHaveLags(0, [120, Nan, Nan, Nan], "today_open (15m): Tuesday's bucket only, not the late bucket's Monday value (100) one bar back");
        frame.ShouldHaveLags(1, [120, Nan, Nan, Nan], "vwap (15m)");
        frame.ShouldHaveLags(2, [130, Nan, Nan, Nan], "prev_day_close (15m)");
        frame.ShouldHaveLags(3, [100, Nan, Nan, Nan], "prev_day_open (15m)");
        var ema = 105 + 2.0 / 3 * (130 - 105);
        frame.ShouldHaveLags(4, [ema + 2.0 / 3 * (120 - ema), ema, 105, Nan], "ema(2): both closes are bars of the rolling series");
        frame.ShouldHaveLags(5, [120, 130, 110, 100], "raw close: both closes in order");

        // the next bucket closes at 09:44: S = 2 for the lane
        for (var minute = 15; minute < 30; minute++)
        {
            frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + minute, 90));
        }

        frame.ClosedBarsAt(Timeframe.M15).ShouldBe(1);
        frame.ShouldHaveLags(0, [120, 120, Nan, Nan], "today_open (15m) with two buckets");
        frame.ShouldHaveLags(1, [(120 * 1.0 + 90 * 15) / 16, 120, Nan, Nan], "vwap (15m): volume 10 per minute, the first bucket holds one minute");
        frame.ShouldHaveLags(2, [130, 130, Nan, Nan], "prev_day_close (15m)");
    }

    [Fact]
    public void A_session_whose_first_minutes_are_missing_starts_at_the_first_bar_present_on_the_minute_lane_and_at_the_first_closed_bucket_on_a_coarser_one()
    {
        // Tuesday's bars start at 09:30 (the 09:15 bucket never exists). 1m: S = 1 at 09:30. 15m: the bucket 09:30-09:44 closes at 09:44. Monday is one full 15m bucket (09:15-09:29, price 100) so that prev_day exists.
        var frame = AdversarialFrames.Create([AdversarialFrames.Spec("today_open", Timeframe.M1), AdversarialFrames.Spec("today_open", Timeframe.M15), AdversarialFrames.Spec("prev_day_close", Timeframe.M15)], 4);
        foreach (var i in Enumerable.Range(0, 15))
        {
            frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + i, 100));
        }

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + 15, 140));

        frame.ShouldHaveLags(0, [140, Nan, Nan, Nan], "1m today_open at the first bar present");
        frame.ShouldHaveLags(1, [Nan, Nan, Nan, Nan], "15m today_open is pending until the 09:30 bucket closes");

        foreach (var i in Enumerable.Range(16, 14))
        {
            frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + i, 140));
        }

        frame.ShouldHaveLags(0, [140, 140, 140, 140], "1m today_open after 15 bars");
        frame.ShouldHaveLags(1, [140, Nan, Nan, Nan], "15m today_open after its first bucket");
        frame.ShouldHaveLags(2, [100, Nan, Nan, Nan], "15m prev_day_close");
    }
}
