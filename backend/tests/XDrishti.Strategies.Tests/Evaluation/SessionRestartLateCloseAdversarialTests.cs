using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// ISS-13, adversarial, through the language: the 15:29 minute of Monday is missing, so Monday's last 15-minute bucket (15:15, price 130, 14 minutes) closes only when Tuesday's first minute arrives — late, and with
/// Monday's date. A 15m strategy evaluated at every minute: Monday's buckets are 100 (14:45), 110 (15:00) and the late 130; Tuesday's are 120 and 90. The evaluation at the step of the late close is Tuesday's, so every
/// per-session feature of the 15m lane is unknown there (pending) while the continuous ones have taken the late bar; the lane is in its Tuesday session from the minute its own first bucket closes. Ignored bars (a
/// duplicate, an older bar) change none of it.
/// </summary>
public sealed class SessionRestartLateCloseAdversarialTests
{
    private static readonly string[] Conditions =
    [
        "vwap > 0",
        "close > 0",
        "prev_day_close == 130",
        "prev_day_close[1] == 130",
        "today_open[1] == 120",
        "ema(2) > 0",
    ];

    private static Bar[] MondayWithoutTheLastMinute() =>
    [
        .. Enumerable.Range(0, 15).Select(i => Clock.Close(Clock.Mon, Clock.At(14, 45) + i, 100)),
        .. Enumerable.Range(0, 15).Select(i => Clock.Close(Clock.Mon, Clock.At(15, 0) + i, 110)),
        .. Enumerable.Range(0, 14).Select(i => Clock.Close(Clock.Mon, Clock.At(15, 15) + i, 130)),
    ];

    private static void ShouldBe(Truth[][] truths, int condition, params (int From, int To, Truth Truth)[] ranges)
    {
        var expected = new Truth[truths.Length];
        foreach (var (from, to, truth) in ranges)
        {
            for (var i = from; i <= to; i++)
            {
                expected[i] = truth;
            }
        }

        for (var i = 0; i < truths.Length; i++)
        {
            truths[i][condition].ShouldBe(expected[i], $"{Conditions[condition]} at step {i}");
        }
    }

    [Fact]
    public void The_step_that_closes_the_late_bucket_is_tuesdays_and_every_per_session_feature_of_the_lane_is_unknown_in_it()
    {
        // Steps: Monday 0-43 (the buckets close at 14, 29 and, late, at step 44); Tuesday 44-73 (09:15 to 09:44; the first bucket closes at 58, the second at 73).
        var rig = Rig.Of(Timeframe.M15, Conditions);
        var bars = new List<Bar>(MondayWithoutTheLastMinute());
        bars.AddRange(Enumerable.Range(0, 15).Select(i => Clock.Close(Clock.Tue, Clock.Open + i, 120)));
        bars.AddRange(Enumerable.Range(15, 15).Select(i => Clock.Close(Clock.Tue, Clock.Open + i, 90)));
        var truths = bars.Select(bar => rig.Step(bar)).ToArray();

        truths.Length.ShouldBe(74);
        ShouldBe(truths, 0, (0, 13, Truth.Unknown), (14, 43, Truth.True), (44, 57, Truth.Unknown), (58, 73, Truth.True));
        ShouldBe(truths, 1, (0, 13, Truth.Unknown), (14, 73, Truth.True));
        ShouldBe(truths, 2, (0, 57, Truth.Unknown), (58, 73, Truth.True));
        ShouldBe(truths, 3, (0, 72, Truth.Unknown), (73, 73, Truth.True));

        // Monday has two bars from step 29 on, so today_open[1] is Monday's first open (100, not 120: false); Tuesday's second bar is the first with a lag 1 of Tuesday's own
        ShouldBe(truths, 4, (0, 28, Truth.Unknown), (29, 43, Truth.False), (44, 72, Truth.Unknown), (73, 73, Truth.True));
        ShouldBe(truths, 5, (0, 28, Truth.Unknown), (29, 73, Truth.True));
    }

    [Fact]
    public void A_first_minute_that_is_the_last_of_its_bucket_closes_the_late_bucket_and_its_own_in_one_step_and_the_new_date_has_one_bar()
    {
        // Tuesday's first minute is 09:29 (price 120), then 09:30-09:44 (90). Step 44 closes Monday's late bucket and Tuesday's one-minute bucket: the lane has one bar of Tuesday and prev_day_close is Monday's late close.
        // The second Tuesday bucket closes at step 59 (09:44): two bars, lag 1 exists.
        var rig = Rig.Of(Timeframe.M15, Conditions);
        var bars = new List<Bar>(MondayWithoutTheLastMinute()) { Clock.Close(Clock.Tue, Clock.Open + 14, 120) };
        bars.AddRange(Enumerable.Range(15, 15).Select(i => Clock.Close(Clock.Tue, Clock.Open + i, 90)));
        var closedAt = new List<int>();
        var truths = bars.Select(bar =>
        {
            var truth = rig.Step(bar);
            closedAt.Add(rig.Frame.ClosedBarsAt(Timeframe.M15));
            return truth;
        }).ToArray();

        truths.Length.ShouldBe(60);
        closedAt[44].ShouldBe(2, "the first Tuesday minute closes Monday's late bucket and Tuesday's own");
        closedAt[59].ShouldBe(1);
        ShouldBe(truths, 0, (0, 13, Truth.Unknown), (14, 59, Truth.True));
        ShouldBe(truths, 2, (0, 43, Truth.Unknown), (44, 59, Truth.True));
        ShouldBe(truths, 3, (0, 58, Truth.Unknown), (59, 59, Truth.True));
        ShouldBe(truths, 4, (0, 28, Truth.Unknown), (29, 43, Truth.False), (44, 58, Truth.Unknown), (59, 59, Truth.True));
        ShouldBe(truths, 5, (0, 28, Truth.Unknown), (29, 59, Truth.True));
    }

    [Fact]
    public void Bars_the_ordering_guard_skips_do_not_make_the_lane_pending_again()
    {
        // After Tuesday's first bucket has closed (step 58, the lane is in its session), an older Monday bar and a duplicate of Tuesday's last bar arrive and are skipped; a restart on a skipped bar would put the lane back
        // into the pending state and make the per-session features unknown for the next fourteen minutes.
        var rig = Rig.Of(Timeframe.M15, Conditions);
        var steps = new List<Bar>(MondayWithoutTheLastMinute());
        steps.AddRange(Enumerable.Range(0, 15).Select(i => Clock.Close(Clock.Tue, Clock.Open + i, 120)));
        steps.Add(Clock.Close(Clock.Mon, Clock.At(15, 20), 999));
        steps.Add(Clock.Close(Clock.Tue, Clock.Open + 14, 777));
        steps.AddRange(Enumerable.Range(15, 15).Select(i => Clock.Close(Clock.Tue, Clock.Open + i, 90)));

        var truths = steps.Select(bar => rig.Step(bar)).ToArray();

        rig.Frame.IgnoredBars.ShouldBe(2);
        for (var i = 59; i < truths.Length; i++)
        {
            truths[i][0].ShouldBe(Truth.True, $"vwap > 0 at step {i}: the lane stays in Tuesday's session");
            truths[i][2].ShouldBe(Truth.True, $"prev_day_close == 130 at step {i}");
        }

        truths[^1][4].ShouldBe(Truth.True, "today_open[1] == 120 once the second bucket has closed");
    }
}
