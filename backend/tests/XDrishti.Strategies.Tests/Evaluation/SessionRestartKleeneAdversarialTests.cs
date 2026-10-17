using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// ISS-13, adversarial: three-valued logic over lanes that are pending at different times of a session. Three sessions of three whole hours with flat bars (every minute of an hour at one price). The 1m lane is in its
/// session from the first minute, the 15m lane from the 15th minute (its first bucket closes at minute 14), the 1h lane from the 60th (minute 59). Kleene: false and unknown is false, true or unknown is true, not unknown is
/// unknown. Date-driven features sit beside the restarting ones and are never pending.
/// </summary>
public sealed class SessionRestartKleeneAdversarialTests
{
    private static readonly double[][] Prices = [[100, 110, 130], [120, 90, 140], [150, 150, 150]];

    private static readonly DateOnly[] Dates = [Clock.Mon, Clock.Tue, Clock.Wed];

    private static Bar[] Bars() =>
    [
        .. Enumerable.Range(0, 3).SelectMany(s => Enumerable.Range(0, 180).Select(minute => Clock.Close(Dates[s], Clock.Open + minute, Prices[s][minute / 60]))),
    ];

    [Fact]
    public void The_kleene_tables_hold_over_lanes_that_become_known_at_different_minutes_of_a_session()
    {
        var conditions = new[]
        {
            "tf(\"1h\").today_open > 0",
            "tf(\"15m\").today_open > 0",
            "close > 0 or tf(\"1h\").today_open > 0",
            "close > 0 and tf(\"1h\").today_open > 0",
            "close < 0 and tf(\"1h\").today_open > 0",
            "not (tf(\"1h\").vwap > 0)",
            "not (tf(\"1h\").vwap > 0) or tf(\"15m\").vwap > 0",
            "between(tf(\"15m\").today_high, 0, 1000) and between(tf(\"1h\").today_low, 0, 1000)",
        };
        var rig = Rig.Of(Timeframe.M1, conditions);
        var bars = Bars();

        for (var i = 0; i < bars.Length; i++)
        {
            var minute = i % 180;
            var hourKnown = minute >= 59;
            var quarterKnown = minute >= 14;
            var truths = rig.Step(bars[i]);
            var where = $"session {i / 180} minute {minute}";

            truths[0].ShouldBe(hourKnown ? Truth.True : Truth.Unknown, "1h today_open > 0 at " + where);
            truths[1].ShouldBe(quarterKnown ? Truth.True : Truth.Unknown, "15m today_open > 0 at " + where);
            truths[2].ShouldBe(Truth.True, "true or unknown is true at " + where);
            truths[3].ShouldBe(hourKnown ? Truth.True : Truth.Unknown, "true and unknown is unknown at " + where);
            truths[4].ShouldBe(Truth.False, "false and unknown is false at " + where);
            truths[5].ShouldBe(hourKnown ? Truth.False : Truth.Unknown, "not unknown is unknown at " + where);
            truths[6].ShouldBe(hourKnown ? Truth.True : quarterKnown ? Truth.True : Truth.Unknown, "(not vwap1h) or vwap15m at " + where);
            truths[7].ShouldBe(hourKnown ? Truth.True : Truth.Unknown, "between over a 15m and a 1h value at " + where);
        }
    }

    [Fact]
    public void A_restarting_lag_beside_a_date_driven_feature_is_unknown_exactly_where_the_lag_reaches_across_the_open()
    {
        // day_of_week == TUE is false all Monday and true all Tuesday; vwap[1] > 0 is unknown on the first bar of each session and true afterwards (prices are well above zero). Monday: false and (U T T) is false throughout;
        // Tuesday: true and (U T T T) is U T T T. The old frame answered true on Tuesday's first bar, reading Monday's last vwap.
        var rig = Rig.Of(Timeframe.M1, "day_of_week == TUE and vwap[1] > 0");
        var bars = new[]
        {
            Clock.Bar(Clock.Mon, Clock.Open, 100, 100, 100, 100), Clock.Close(Clock.Mon, Clock.Open + 1, 98), Clock.Close(Clock.Mon, Clock.Open + 2, 96),
            Clock.Bar(Clock.Tue, Clock.Open, 119, 123, 117, 122), Clock.Close(Clock.Tue, Clock.Open + 1, 121), Clock.Close(Clock.Tue, Clock.Open + 2, 119), Clock.Close(Clock.Tue, Clock.Open + 3, 130),
        };

        rig.Run(bars).ShouldBe("FFFUTTT");
    }

    [Fact]
    public void Two_conditions_sharing_one_frame_and_the_same_pending_lane_agree_with_the_conditions_alone()
    {
        // the merged plan reads one frame; a condition must answer the same whether it runs alone or beside others that read other lanes and lags
        var alone = Rig.Of(Timeframe.M1, "crosses_above(close, vwap)");
        var together = Rig.Of(Timeframe.M1, ["crosses_above(close, vwap)", "tf(\"15m\").prev_day_close > 0", "rising(tf(\"1h\").vwap, 1)", "close[3] > today_open[2]"]);
        var series = Series.Sessions(8, 3);

        foreach (var bar in series)
        {
            var single = alone.Step(bar)[0];
            var merged = together.Step(bar);

            merged[0].ShouldBe(single, $"at {bar.EpochSeconds}");
        }
    }
}
