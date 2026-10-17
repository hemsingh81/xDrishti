using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// ISS-13, adversarial: three whole seeded sessions on a 1-minute strategy reading a per-session feature of the 1m, 5m, 15m and 1h lanes at lags 1, 2 and 5. With <c>S</c> the number of bars the lane has closed in the
/// session at the minute <c>j</c> (0-based; a lane closes a bar at every <c>T</c>-th minute and the last, partial, bar at minute 374, so S = floor((j + 1) / T) before the last minute and ceil(375 / T) at it), the
/// truth of every condition below follows from the arithmetic alone, not from the frame: a feature that is constant within a session compared with itself k bars ago is unknown exactly when S &lt;= k, and true otherwise;
/// a crossing is unknown exactly when S &lt; 2. Nothing here is ever the other session's value.
/// </summary>
public sealed class SessionRestartArithmeticAdversarialTests
{
    private static readonly Timeframe[] Lanes = [Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1];

    private static readonly int[] Lags = [1, 2, 5];

    public static TheoryData<Timeframe, int> LaneAndLag
    {
        get
        {
            var data = new TheoryData<Timeframe, int>();
            foreach (var lane in Lanes)
            {
                foreach (var lag in Lags)
                {
                    data.Add(lane, lag);
                }
            }

            return data;
        }
    }

    private static string Prefix(Timeframe lane) => lane == Timeframe.M1 ? string.Empty : "tf(\"" + lane.ToCode() + "\").";

    private static int Closed(Timeframe lane, int minute)
    {
        var length = lane.Minutes();
        return minute == 374 ? (375 + length - 1) / length : (minute + 1) / length;
    }

    [Theory]
    [MemberData(nameof(LaneAndLag))]
    public void A_session_constant_read_k_bars_back_is_unknown_exactly_while_the_session_has_k_bars_or_fewer_and_true_afterwards(Timeframe lane, int lag)
    {
        var p = Prefix(lane);
        var conditions = new[]
        {
            $"{p}today_open[{lag}] == {p}today_open",
            $"{p}prev_day_close[{lag}] == {p}prev_day_close",
            $"{p}gap_pct[{lag}] == {p}gap_pct",
            $"{p}prev_day_high[{lag}] == {p}prev_day_high",
            $"{p}today_low[{lag}] >= {p}today_low",
            $"{p}today_high[{lag}] <= {p}today_high",
        };
        var rig = Rig.Of(Timeframe.M1, conditions);
        var bars = Series.Sessions(23, 3);

        for (var i = 0; i < bars.Length; i++)
        {
            var session = i / 375;
            var closed = Closed(lane, i % 375);
            var truths = rig.Step(bars[i]);

            for (var c = 0; c < conditions.Length; c++)
            {
                // the first session has no previous day: the prev_day and gap features are never known in it
                var knowable = c is 0 or 4 or 5 || session > 0;
                var expected = knowable && closed > lag ? Truth.True : Truth.Unknown;
                truths[c].ShouldBe(expected, $"{conditions[c]}: session {session} minute {i % 375} ({closed} bar(s) closed on {lane.ToCode()})");
            }
        }
    }

    [Theory]
    [MemberData(nameof(LaneAndLag))]
    public void A_constant_that_is_compared_for_rising_is_unknown_while_the_session_is_too_short_and_false_afterwards_never_true(Timeframe lane, int lag)
    {
        var p = Prefix(lane);
        var conditions = new[] { $"rising({p}today_open, {lag})", $"falling({p}today_open, {lag})", $"rising({p}prev_day_close, {lag})" };
        var rig = Rig.Of(Timeframe.M1, conditions);
        var bars = Series.Sessions(31, 3);

        for (var i = 0; i < bars.Length; i++)
        {
            var session = i / 375;
            var closed = Closed(lane, i % 375);
            var truths = rig.Step(bars[i]);

            for (var c = 0; c < conditions.Length; c++)
            {
                var knowable = c < 2 || session > 0;
                var expected = knowable && closed > lag ? Truth.False : Truth.Unknown;
                truths[c].ShouldBe(expected, $"{conditions[c]}: session {session} minute {i % 375} ({closed} bar(s) closed on {lane.ToCode()})");
            }
        }
    }

    [Theory]
    [InlineData(Timeframe.M1)]
    [InlineData(Timeframe.M5)]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.H1)]
    public void A_crossing_of_a_session_value_is_unknown_exactly_while_the_session_has_fewer_than_two_bars_on_the_lane(Timeframe lane)
    {
        var p = Prefix(lane);
        var conditions = new[]
        {
            $"crosses_above({p}close, {p}today_open)",
            $"crosses_below({p}close, {p}today_open)",
            $"crosses_above({p}close, {p}today_high)",
            $"crosses_below({p}close, {p}today_low)",
            $"crosses_above({p}close, {p}prev_day_high)",
            $"crosses_below({p}close, {p}prev_day_low)",
            $"not crosses_above({p}close, {p}today_open)",
        };
        var rig = Rig.Of(Timeframe.M1, conditions);
        var bars = Series.Sessions(47, 4);
        var everTrue = new bool[conditions.Length];

        for (var i = 0; i < bars.Length; i++)
        {
            var session = i / 375;
            var closed = Closed(lane, i % 375);
            var truths = rig.Step(bars[i]);

            for (var c = 0; c < conditions.Length; c++)
            {
                var knowable = c is not (4 or 5) || session > 0;
                if (knowable && closed >= 2)
                {
                    truths[c].ShouldNotBe(Truth.Unknown, $"{conditions[c]}: session {session} minute {i % 375} ({closed} bar(s) closed on {lane.ToCode()}) has every value it reads");
                }
                else
                {
                    truths[c].ShouldBe(Truth.Unknown, $"{conditions[c]}: session {session} minute {i % 375} ({closed} bar(s) closed on {lane.ToCode()}) must be unknown");
                }

                everTrue[c] |= truths[c] == Truth.True;
            }
        }

        // the unknown is not the whole answer: the in-session crossings do happen on a random walk of four sessions
        everTrue[0].ShouldBeTrue("crosses_above(close, today_open) is true somewhere in the sessions");
        everTrue[1].ShouldBeTrue("crosses_below(close, today_open) is true somewhere in the sessions");
    }

    [Theory]
    [InlineData(Timeframe.M1)]
    [InlineData(Timeframe.M5)]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.H1)]
    public void An_opening_range_of_three_bars_is_known_from_the_third_bar_and_its_history_from_the_fourth(Timeframe lane)
    {
        // or_high(3 bars of the lane) closes with the third bar of the session and is constant afterwards. Its previous value exists from the fourth bar on: a crossing against it or a run over it is unknown while the session has
        // fewer than 4 bars (the old frame read the previous session's range at the second and third bars), rising(x, 2) while it has fewer than 5; and a constant is never rising. The range is a call: it can only be read, not subscripted.
        var minutes = 3 * lane.Minutes();
        var p = Prefix(lane);
        var conditions = new[]
        {
            $"{p}or_high({minutes}) > 0",
            $"crosses_above({p}close, {p}or_high({minutes}))",
            $"crosses_below({p}close, {p}or_low({minutes}))",
            $"rising({p}or_high({minutes}), 1)",
            $"falling({p}or_low({minutes}), 2)",
        };
        var rig = Rig.Of(Timeframe.M1, conditions);
        var bars = Series.Sessions(61, 3);
        var everKnown = new bool[conditions.Length];

        for (var i = 0; i < bars.Length; i++)
        {
            var closed = Closed(lane, i % 375);
            var truths = rig.Step(bars[i]);

            truths[0].ShouldBe(closed >= 3 ? Truth.True : Truth.Unknown, $"{conditions[0]}: session {i / 375} minute {i % 375} ({closed} bar(s) closed)");
            truths[1].ShouldBe(closed >= 4 ? truths[1] : Truth.Unknown, $"{conditions[1]}: session {i / 375} minute {i % 375} ({closed} bar(s) closed)");
            truths[2].ShouldBe(closed >= 4 ? truths[2] : Truth.Unknown, $"{conditions[2]}: session {i / 375} minute {i % 375} ({closed} bar(s) closed)");
            truths[3].ShouldBe(closed >= 4 ? Truth.False : Truth.Unknown, $"{conditions[3]}: session {i / 375} minute {i % 375} ({closed} bar(s) closed)");
            truths[4].ShouldBe(closed >= 5 ? Truth.False : Truth.Unknown, $"{conditions[4]}: session {i / 375} minute {i % 375} ({closed} bar(s) closed)");
            if (closed >= 4)
            {
                truths[1].ShouldNotBe(Truth.Unknown, $"{conditions[1]} has every value it reads: session {i / 375} minute {i % 375}");
                truths[2].ShouldNotBe(Truth.Unknown, $"{conditions[2]} has every value it reads: session {i / 375} minute {i % 375}");
            }

            for (var c = 0; c < conditions.Length; c++)
            {
                everKnown[c] |= truths[c] != Truth.Unknown;
            }
        }

        everKnown.ShouldAllBe(known => known);
    }

    [Theory]
    [InlineData(Timeframe.M1)]
    [InlineData(Timeframe.M5)]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.H1)]
    public void The_headline_crossing_of_the_vwap_is_never_true_before_the_second_bar_of_a_session_on_any_lane(Timeframe lane)
    {
        // crosses_above(close, vwap) on a 1m strategy reading the lane's close and vwap. 500 sessions would be the review's measurement; 40 are enough here: the old frame answered true on the first bar of about a quarter.
        var p = Prefix(lane);
        var rig = Rig.Of(Timeframe.M1, [$"crosses_above({p}close, {p}vwap)", $"{p}close > {p}vwap and {p}close[1] <= {p}vwap[1]", $"rising({p}vwap, 1)"]);
        var bars = Series.Sessions(5, 40);
        var firstEvaluationTrue = 0;
        var inSessionTrue = new int[3];

        for (var i = 0; i < bars.Length; i++)
        {
            var closed = Closed(lane, i % 375);
            var truths = rig.Step(bars[i]);

            if (closed < 2)
            {
                // "never true" is the requirement; the first bar of a lane with one bar closed or none must not be a cross of two sessions
                truths[0].ShouldNotBe(Truth.True, $"crosses_above at session {i / 375} minute {i % 375} ({closed} bar(s) closed)");
                truths[1].ShouldNotBe(Truth.True, $"expression form at session {i / 375} minute {i % 375} ({closed} bar(s) closed)");
                truths[2].ShouldNotBe(Truth.True, $"rising(vwap, 1) at session {i / 375} minute {i % 375} ({closed} bar(s) closed)");
                firstEvaluationTrue += i % 375 == 0 && truths[0] == Truth.True ? 1 : 0;
            }
            else
            {
                for (var c = 0; c < 3; c++)
                {
                    inSessionTrue[c] += truths[c] == Truth.True ? 1 : 0;
                }
            }
        }

        firstEvaluationTrue.ShouldBe(0);
        inSessionTrue.ShouldAllBe(count => count > 0);
    }
}
