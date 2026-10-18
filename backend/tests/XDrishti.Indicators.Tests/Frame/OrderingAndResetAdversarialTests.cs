using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Frame;

/// <summary>
/// ISS-13, adversarial: a bar the ordering guard skips restarts nothing, and <see cref="FeatureFrame.Reset"/> returns to "no date seen" wherever it happens (mid-session, right after a date change, while a coarser lane is
/// pending), so a reset frame is indistinguishable from a fresh one at every step and every lag.
/// </summary>
public sealed class OrderingAndResetAdversarialTests
{
    private static readonly double Nan = double.NaN;

    public static TheoryData<Timeframe, int> TimeframesAndSeeds
    {
        get
        {
            var data = new TheoryData<Timeframe, int>();
            foreach (var timeframe in new[] { Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1 })
            {
                foreach (var seed in new[] { 5, 17, 41 })
                {
                    data.Add(timeframe, seed);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(TimeframesAndSeeds))]
    public void Duplicates_and_older_bars_around_a_date_change_leave_every_value_and_every_lag_as_in_the_clean_series(Timeframe timeframe, int seed)
    {
        var clean = SeriesGenerator.Random(seed, 700, 1_600, specialSessions: true);
        var specs = TestSpecs.Variants(timeframe);
        var dirtyFrame = AdversarialFrames.Create(specs);
        var cleanFrame = AdversarialFrames.Create(specs);
        var injected = 0;

        for (var i = 0; i < clean.Count; i++)
        {
            dirtyFrame.Step(clean[i]);
            cleanFrame.Step(clean[i]);
            AdversarialFrames.AssertSameState(dirtyFrame, cleanFrame, $"{timeframe.ToCode()} seed {seed} bar {i} ({AdversarialFrames.StepName(clean[i])})");

            var firstOfDate = i == 0 || SessionClock.DateOf(clean[i].EpochSeconds) != SessionClock.DateOf(clean[i - 1].EpochSeconds);
            var extras = new List<Bar>();
            if (firstOfDate)
            {
                // a duplicate of the first bar of the date, and the last bar of the date before (older than the one accepted)
                extras.Add(clean[i]);
                if (i > 0)
                {
                    extras.Add(clean[i - 1]);
                }
            }
            else if (i % 53 == 7)
            {
                extras.Add(clean[i]);
                extras.Add(clean[i - 1]);
            }

            foreach (var extra in extras)
            {
                dirtyFrame.Step(extra);
                injected++;

                AdversarialFrames.AssertSameState(dirtyFrame, cleanFrame, $"{timeframe.ToCode()} seed {seed} after a skipped bar following bar {i} ({AdversarialFrames.StepName(clean[i])})");
                dirtyFrame.BarClosed(timeframe).ShouldBeFalse("a skipped bar closes nothing on any lane");
            }
        }

        dirtyFrame.IgnoredBars.ShouldBe(injected);
        cleanFrame.IgnoredBars.ShouldBe(0);
    }

    [Fact]
    public void A_duplicate_of_the_first_bar_of_a_date_is_counted_does_not_clear_the_history_again_and_changes_no_value()
    {
        // 1m, depth 4. Monday M1 M2; Tuesday T1, T1 again (ignored), T2. today_open: 100 100 | 110 110 (T1 twice) 110. After T2 the lags are [110, 110, NaN, NaN]; a second clear would not change this, but a
        // restart of the session by the duplicate would make the vwap start over: vwap after T2 must be the mean of T1 and T2 (plus volume weights), not T2 alone.
        var specs = new List<FeatureSpec> { AdversarialFrames.Spec("today_open", Timeframe.M1), AdversarialFrames.Spec("vwap", Timeframe.M1), AdversarialFrames.Spec("prev_day_close", Timeframe.M1) };
        var frame = AdversarialFrames.Create(specs, 4);
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open, 100));
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 1, 104));
        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open, 110));

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open, 999, 500));

        frame.IgnoredBars.ShouldBe(1);
        frame.Steps.ShouldBe(3);
        frame.ShouldHaveLags(0, [110, Nan, Nan, Nan], "today_open after the duplicate");
        frame.ShouldHaveLags(1, [110, Nan, Nan, Nan], "vwap after the duplicate");
        frame.ShouldHaveLags(2, [104, Nan, Nan, Nan], "prev_day_close after the duplicate");

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + 1, 120));

        frame.ShouldHaveLags(0, [110, 110, Nan, Nan], "today_open");
        frame.ShouldHaveLags(1, [115, 110, Nan, Nan], "vwap (110 + 120) / 2");
        frame.ShouldHaveLags(2, [104, 104, Nan, Nan], "prev_day_close");
    }

    [Fact]
    public void An_older_bar_of_the_previous_date_arriving_after_the_first_bar_of_a_date_does_not_restart_or_wipe_anything()
    {
        // Tuesday has two bars; then a Monday bar (09:17, after Monday's 09:16 but before everything Tuesday): older than the last accepted bar, skipped. Tuesday's third bar: lags 0..2 are its own three bars.
        var frame = AdversarialFrames.Create([AdversarialFrames.Spec("vwap", Timeframe.M1), AdversarialFrames.Spec("prev_day_close", Timeframe.M1)], 4);
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open, 100));
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 1, 104));
        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open, 110));
        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + 1, 120));

        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 2, 5_000, 1_000));

        frame.IgnoredBars.ShouldBe(1);
        frame.ShouldHaveLags(0, [115, 110, Nan, Nan], "vwap after a Monday bar was skipped");
        frame.ShouldHaveLags(1, [104, 104, Nan, Nan], "prev_day_close is still Monday's last accepted close");

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + 2, 130));

        frame.ShouldHaveLags(0, [120, 115, 110, Nan], "vwap, three bars of Tuesday");
        frame.ShouldHaveLags(1, [104, 104, 104, Nan], "prev_day_close");
    }

    // ---- Reset -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    public static TheoryData<Timeframe, int> ResetPoints
    {
        get
        {
            // bar indexes of three full sessions: 0 before the first bar, 1, 100, 374 last bar of Monday, 375 first bar of Tuesday, 376, 380 (a 15m and 1h lane is pending), 389 (15m closes its first Tuesday bucket),
            // 434 (1h still pending), 749 last bar of Tuesday, 750 first bar of Wednesday, 800
            var data = new TheoryData<Timeframe, int>();
            foreach (var timeframe in new[] { Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1 })
            {
                foreach (var cut in new[] { 0, 1, 100, 374, 375, 376, 380, 389, 434, 749, 750, 800 })
                {
                    data.Add(timeframe, cut);
                }
            }

            return data;
        }
    }

    private static List<FeatureSpec> ResetSpecs(Timeframe timeframe)
    {
        var specs = AdversarialFrames.TwelveOn(timeframe, 2 * Math.Max(1, timeframe.Minutes()));
        specs.AddRange(AdversarialFrames.TwelveOn(timeframe).Where(s => s.Name is "or_high" or "or_low"));
        specs.Add(AdversarialFrames.Spec("ema", timeframe, 3));
        specs.Add(AdversarialFrames.Spec("close", timeframe));
        return specs;
    }

    [Theory]
    [MemberData(nameof(ResetPoints))]
    public void A_frame_reset_at_any_point_and_fed_the_series_again_equals_a_fresh_frame_at_every_step(Timeframe timeframe, int cut)
    {
        var bars = AdversarialFrames.ThreeFullSessions(7);
        var specs = ResetSpecs(timeframe);
        var used = AdversarialFrames.Create(specs);
        for (var i = 0; i < cut; i++)
        {
            used.Step(bars[i]);
        }

        used.Reset();
        var fresh = AdversarialFrames.Create(specs);

        AdversarialFrames.AssertSameState(used, fresh, $"{timeframe.ToCode()} cut {cut}: right after Reset");
        for (var i = 0; i < bars.Count; i++)
        {
            used.Step(bars[i]);
            fresh.Step(bars[i]);

            AdversarialFrames.AssertSameState(used, fresh, $"{timeframe.ToCode()} cut {cut}: replay bar {i} ({AdversarialFrames.StepName(bars[i])})");
        }
    }

    [Theory]
    [MemberData(nameof(ResetPoints))]
    public void A_frame_reset_in_the_middle_of_a_day_and_fed_from_mid_day_equals_a_fresh_frame_fed_the_same_bars(Timeframe timeframe, int cut)
    {
        // after the reset the frame never saw the earlier dates: the first bar it is given (the one at index cut + 3 — a minute the reset frame is fed from mid-day) opens its session
        var bars = AdversarialFrames.ThreeFullSessions(9);
        var specs = ResetSpecs(timeframe);
        var used = AdversarialFrames.Create(specs);
        for (var i = 0; i < cut; i++)
        {
            used.Step(bars[i]);
        }

        used.Reset();
        var fresh = AdversarialFrames.Create(specs);
        var from = Math.Min(cut + 3, bars.Count - 1);

        for (var i = from; i < bars.Count; i++)
        {
            used.Step(bars[i]);
            fresh.Step(bars[i]);

            AdversarialFrames.AssertSameState(used, fresh, $"{timeframe.ToCode()} cut {cut}: fed from bar {from}, bar {i} ({AdversarialFrames.StepName(bars[i])})");
        }
    }

    [Fact]
    public void A_frame_fed_from_mid_day_takes_the_date_of_its_first_bar_as_its_session_and_knows_nothing_before_it()
    {
        // 1m. The first bar the frame sees is Tuesday's second bar (112, 113, 105, 106, volume 50): its session starts there. Wednesday's prev_day_* are those of the bars it saw: open 112, high max(113, 120), low min(105, 104),
        // close 118, volume 50 + 60.
        var frame = AdversarialFrames.Create(
        [
            AdversarialFrames.Spec("vwap", Timeframe.M1), AdversarialFrames.Spec("prev_day_close", Timeframe.M1), AdversarialFrames.Spec("today_open", Timeframe.M1), AdversarialFrames.Spec("prev_day_open", Timeframe.M1),
            AdversarialFrames.Spec("prev_day_high", Timeframe.M1), AdversarialFrames.Spec("prev_day_low", Timeframe.M1), AdversarialFrames.Spec("prev_day_volume", Timeframe.M1),
        ], 4);

        frame.Step(Minutes.Bar(BarFixtures.Tue, Minutes.Open + 1, 112, 113, 105, 106, 50));

        frame.ShouldHaveLags(0, [324.0 / 3, Nan, Nan, Nan], "vwap of the first bar the frame saw");
        frame.ShouldHaveLags(1, [Nan, Nan, Nan, Nan], "no previous session");
        frame.ShouldHaveLags(2, [112, Nan, Nan, Nan], "today_open");

        frame.Step(Minutes.Bar(BarFixtures.Tue, Minutes.Open + 2, 106, 120, 104, 118, 60));
        frame.Step(Minutes.Bar(BarFixtures.Wed, Minutes.Open, 120, 126, 119, 125, 5));

        frame.ShouldHaveLags(1, [118, Nan, Nan, Nan], "prev_day_close on Wednesday");
        frame.ShouldHaveLags(3, [112, Nan, Nan, Nan], "prev_day_open on Wednesday is the open of the first bar fed");
        frame.ShouldHaveLags(4, [120, Nan, Nan, Nan], "prev_day_high");
        frame.ShouldHaveLags(5, [104, Nan, Nan, Nan], "prev_day_low");
        frame.ShouldHaveLags(6, [110, Nan, Nan, Nan], "prev_day_volume");
        frame.ShouldHaveLags(0, [370.0 / 3, Nan, Nan, Nan], "vwap restarts on Wednesday");
    }

    [Fact]
    public void A_frame_reset_while_a_coarser_lane_is_pending_and_fed_a_later_date_has_no_previous_session()
    {
        // 15m. Monday 09:15-09:29 at 100 and Tuesday 09:15-09:20 (the 15m lane is pending), then Reset, then Wednesday 09:15-09:29 at 150: prev_day_close must be NaN (the frame never saw Monday or Tuesday), today_open 150.
        var specs = new List<FeatureSpec>
        {
            AdversarialFrames.Spec("today_open", Timeframe.M15), AdversarialFrames.Spec("prev_day_close", Timeframe.M15), AdversarialFrames.Spec("vwap", Timeframe.M15), AdversarialFrames.Spec("today_open", Timeframe.M1),
        };
        var frame = AdversarialFrames.Create(specs, 4);
        foreach (var i in Enumerable.Range(0, 15))
        {
            frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + i, 100));
        }

        foreach (var i in Enumerable.Range(0, 6))
        {
            frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + i, 120));
        }

        frame.IsReady(0).ShouldBeFalse("15m today_open is pending on Tuesday 09:20");

        frame.Reset();
        frame.Steps.ShouldBe(0);
        foreach (var i in Enumerable.Range(0, 14))
        {
            frame.Step(Minutes.Close(BarFixtures.Wed, Minutes.Open + i, 150));

            frame.IsReady(0).ShouldBeFalse($"after Reset the 15m lane has no bar of Wednesday at minute {i}");
            frame.IsReady(1).ShouldBeFalse();
            frame.At(3, 0).ShouldBeNear(150, "1m today_open");
        }

        frame.Step(Minutes.Close(BarFixtures.Wed, Minutes.Open + 14, 150));

        frame.ShouldHaveLags(0, [150, Nan, Nan, Nan], "15m today_open");
        frame.ShouldHaveLags(1, [Nan, Nan, Nan, Nan], "15m prev_day_close: the frame never saw an earlier session");
        frame.ShouldHaveLags(2, [150, Nan, Nan, Nan], "15m vwap");
    }
}
