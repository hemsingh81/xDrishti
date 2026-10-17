using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Frame;

/// <summary>
/// ISS-13, adversarial, the awkward corners of the session rule: two closes inside one date, the pending state seen through <c>TryGet</c>, <c>IsReady</c> and <c>WarmUp</c>, an opening range that a late first bar makes
/// ready at once or never, and weekly and monthly lanes next to restarting ones. Hand-calculated; the long runs use the shared reference.
/// </summary>
public sealed class EdgeSessionsAdversarialTests
{
    private static readonly double Nan = double.NaN;

    [Fact]
    public void A_minute_that_closes_a_late_bucket_and_its_own_inside_one_date_counts_both_bars_in_the_session()
    {
        // 15m. Monday: one full bucket at 90 (09:15-09:29). Tuesday: 09:15 alone at 100 (the bucket 09:15 starts), then 09:44 alone at 130 — the last minute of the bucket 09:30. The step at 09:44 closes the 09:15 bucket (late, 1 minute)
        // and the 09:30 bucket (1 minute): two bars, both of Tuesday. S = 2, so lag 1 is the first of them — not NaN, and not Monday's. Volume 10 per minute, so each bucket weighs the same.
        List<FeatureSpec> specs =
        [
            AdversarialFrames.Spec("today_open", Timeframe.M15), AdversarialFrames.Spec("vwap", Timeframe.M15), AdversarialFrames.Spec("today_high", Timeframe.M15), AdversarialFrames.Spec("prev_day_close", Timeframe.M15),
            AdversarialFrames.Spec("ema", Timeframe.M15, 2),
        ];
        var frame = AdversarialFrames.Create(specs, 4);
        foreach (var i in Enumerable.Range(0, 15))
        {
            frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + i, 90));
        }

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open, 100));

        frame.ClosedBarsAt(Timeframe.M15).ShouldBe(0);
        frame.ShouldHaveLags(0, [Nan, Nan, Nan, Nan], "pending after Tuesday's first minute");

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + 29, 130));

        frame.ClosedBarsAt(Timeframe.M15).ShouldBe(2);
        frame.ShouldHaveLags(0, [100, 100, Nan, Nan], "today_open (15m): two Tuesday bars");
        frame.ShouldHaveLags(1, [115, 100, Nan, Nan], "vwap (15m): (100 + 130) / 2, then 100");
        frame.ShouldHaveLags(2, [130, 100, Nan, Nan], "today_high (15m)");
        frame.ShouldHaveLags(3, [90, 90, Nan, Nan], "prev_day_close (15m): Monday's bucket");
        frame.ShouldHaveLags(4, [95 + 2.0 / 3 * (130 - 95), 95, Nan, Nan], "ema(2) runs on across the sessions: Monday's 90 and Tuesday's 100 seed it (95), Tuesday's 130 follows");
    }

    [Fact]
    public void A_pending_lane_is_seen_as_not_ready_by_every_read_and_its_warm_up_is_still_the_features()
    {
        // 15m, three buckets on Monday (100 110 130), Tuesday's first minute: the lane is pending.
        List<FeatureSpec> specs = [AdversarialFrames.Spec("today_open", Timeframe.M15), AdversarialFrames.Spec("prev_day_close", Timeframe.M15), AdversarialFrames.Spec("ema", Timeframe.M15, 2)];
        var frame = AdversarialFrames.Create(specs, 4);
        var warmUps = Enumerable.Range(0, 3).Select(frame.WarmUp).ToArray();
        foreach (var (price, bucket) in new[] { 100.0, 110, 130 }.Select((p, i) => (p, i)))
        {
            foreach (var i in Enumerable.Range(0, 15))
            {
                frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + bucket * 15 + i, price));
            }
        }

        frame.TryGet(specs[0], out var open).ShouldBeTrue();
        open.ShouldBe(100);

        frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open, 120));

        frame.IsReady(0).ShouldBeFalse();
        frame.IsReady(1).ShouldBeFalse();
        frame.IsReady(2).ShouldBeTrue("a continuous feature on the same lane stays ready");
        frame.TryGet(specs[0], out var pendingOpen).ShouldBeFalse();
        double.IsNaN(pendingOpen).ShouldBeTrue();
        frame.TryGet(specs[1], out _).ShouldBeFalse();
        frame.TryGet(specs[2], out var ema).ShouldBeTrue();
        ema.ShouldBeNear(105 + 2.0 / 3 * (130 - 105), "ema(2) after Monday's three buckets");
        frame.HandleOf(specs[0]).ShouldBe(0, "the frame still knows the feature while it is pending");
        Enumerable.Range(0, 3).Select(frame.WarmUp).ShouldBe(warmUps, "WarmUp is a property of the feature and does not move with the date");
        for (var k = 1; k < 4; k++)
        {
            frame.IsReadyAt(0, k).ShouldBeFalse($"lag {k}");
            frame.IsReadyAt(1, k).ShouldBeFalse($"lag {k}");
        }

        foreach (var i in Enumerable.Range(1, 14))
        {
            frame.Step(Minutes.Close(BarFixtures.Tue, Minutes.Open + i, 120));
        }

        frame.TryGet(specs[0], out var today).ShouldBeTrue();
        today.ShouldBe(120);
        frame.TryGet(specs[1], out var previous).ShouldBeTrue();
        previous.ShouldBe(130);
    }

    [Fact]
    public void An_opening_range_whose_first_bar_is_the_last_minute_of_the_range_is_ready_on_that_bar_and_its_history_starts_there()
    {
        // or_high(15) on 1m: the range is 09:15 to 09:29. Monday's bars 09:15-09:30, one with a high of 105 at 09:20: range 105 (low 99). Tuesday's first bar is 09:29 (high 120, low 108): the range is that one bar,
        // ready at once; 09:30 (high 999, low 1) is outside it. Lag 1 is NaN on the first bar (the session has one) and the range after it. Depth 4.
        List<FeatureSpec> specs = [AdversarialFrames.Spec("or_high", Timeframe.M1, 15), AdversarialFrames.Spec("or_low", Timeframe.M1, 15)];
        var frame = AdversarialFrames.Create(specs, 4);
        for (var i = 0; i <= 15; i++)
        {
            var high = i == 5 ? 105 : 101;
            frame.Step(Minutes.Bar(BarFixtures.Mon, Minutes.Open + i, 100, high, 99, 100));
        }

        frame.ShouldHaveLags(0, [105, 105, Nan, Nan], "Monday's range, ready at the 09:29 bar and constant after it; two bars before that it was not ready");
        frame.ShouldHaveLags(1, [99, 99, Nan, Nan], "Monday's low");

        frame.Step(Minutes.Bar(BarFixtures.Tue, Minutes.Open + 14, 110, 120, 108, 115));

        frame.ShouldHaveLags(0, [120, Nan, Nan, Nan], "or_high on Tuesday's first bar (09:29)");
        frame.ShouldHaveLags(1, [108, Nan, Nan, Nan], "or_low on Tuesday's first bar");

        frame.Step(Minutes.Bar(BarFixtures.Tue, Minutes.Open + 15, 115, 999, 1, 115));
        frame.Step(Minutes.Bar(BarFixtures.Tue, Minutes.Open + 16, 115, 999, 1, 115));

        frame.ShouldHaveLags(0, [120, 120, 120, Nan], "or_high is the 09:29 bar's high; 09:30 and 09:31 are outside the range");
        frame.ShouldHaveLags(1, [108, 108, 108, Nan], "or_low");
    }

    [Fact]
    public void An_opening_range_of_a_session_that_starts_after_the_range_is_never_ready_and_has_no_history_at_all()
    {
        // Wednesday's first bar is 09:35, after the 09:15-09:29 window: the range has no bar, and never will on that date; Monday's range of 105 must not show at any lag.
        List<FeatureSpec> specs = [AdversarialFrames.Spec("or_high", Timeframe.M1, 15)];
        var frame = AdversarialFrames.Create(specs, 4);
        for (var i = 0; i <= 15; i++)
        {
            frame.Step(Minutes.Bar(BarFixtures.Mon, Minutes.Open + i, 100, i == 5 ? 105 : 101, 99, 100));
        }

        frame.ShouldHaveLags(0, [105, 105, Nan, Nan], "Monday's range");

        for (var i = 0; i < 6; i++)
        {
            frame.Step(Minutes.Bar(BarFixtures.Wed, Minutes.Open + 20 + i, 100, 150, 90, 100));

            frame.IsReady(0).ShouldBeFalse($"Wednesday bar {i + 1}");
            frame.ShouldHaveLags(0, [Nan, Nan, Nan, Nan], $"or_high on Wednesday bar {i + 1}");
        }
    }

    // ---- weekly and monthly lanes next to restarting ones -------------------------------------------------------------------------------------------------------------------------------------

    public static TheoryData<int, bool> PeriodSeeds => new() { { 4, false }, { 4, true }, { 19, false }, { 19, true } };

    [Theory]
    [MemberData(nameof(PeriodSeeds))]
    public void Weekly_monthly_and_daily_lanes_run_by_position_beside_restarting_features_on_1m_and_1h(int seed, bool useCalendar)
    {
        var series = SeriesGenerator.Thin(seed, 1_400);
        var specs = new List<FeatureSpec>(AdversarialFrames.TwelveOn(Timeframe.M1));
        specs.AddRange(AdversarialFrames.TwelveOn(Timeframe.H1));
        specs.AddRange(TestSpecs.Variants(Timeframe.D1));
        specs.AddRange(TestSpecs.Variants(Timeframe.W1));
        specs.AddRange(TestSpecs.Variants(Timeframe.Mo1));

        AdversarialReferenceCheck.Run(specs, series.Bars, 4, useCalendar, $"periods seed {seed} calendar {useCalendar}", addAliases: false);
    }
}
