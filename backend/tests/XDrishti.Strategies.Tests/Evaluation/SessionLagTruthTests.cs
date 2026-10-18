using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The rule of ADR 0035 and the arithmetic of <c>lag_across_session</c> checked against the real frame, on the second of two seeded full sessions. <c>today_open</c> is the probe: it is constant inside a session and
/// differs between the two, so <c>today_open[L] != today_open</c> would be <i>true</i> if a lagged read reached into the other session. At the evaluation <c>m</c> minutes after the open (a close of the strategy's
/// timeframe) the feature's timeframe <c>T</c> has closed <c>n = ⌊m / T⌋</c> bars of the session (all <c>⌈375 / T⌉</c> at <c>m</c> = 375, the last one being short): the probe must be <b>unknown exactly when n = 0 (the lane
/// is pending) or L ≥ n</b>, false otherwise, and never true. The lint must warn exactly when the read is unknown at the last evaluation of the session, that is when <c>L · T</c> is at least 375. The counts are
/// computed from the minutes, not from the frame, and the lint's side from the arithmetic.
/// </summary>
public sealed class SessionLagTruthTests
{
    private const int SessionMinutes = SessionClock.RegularBars;

    /// <summary>The strategy timeframe, the timeframe of the feature and the lag; every kind of case: the same timeframe, finer, coarser, a daily strategy, the boundary where the lint starts to warn, the end of the session.</summary>
    public static TheoryData<Timeframe, Timeframe, int> Shapes => new()
    {
        { Timeframe.M1, Timeframe.M1, 1 },
        { Timeframe.M1, Timeframe.M1, 3 },
        { Timeframe.M1, Timeframe.M1, 255 },
        { Timeframe.M5, Timeframe.M5, 1 },
        { Timeframe.M5, Timeframe.M5, 3 },
        { Timeframe.M5, Timeframe.M5, 74 },
        { Timeframe.M5, Timeframe.M5, 75 },
        { Timeframe.M10, Timeframe.M10, 37 },
        { Timeframe.M10, Timeframe.M10, 38 },
        { Timeframe.M15, Timeframe.M15, 1 },
        { Timeframe.M15, Timeframe.M15, 2 },
        { Timeframe.M15, Timeframe.M15, 24 },
        { Timeframe.M15, Timeframe.M15, 25 },
        { Timeframe.M30, Timeframe.M30, 1 },
        { Timeframe.M30, Timeframe.M30, 12 },
        { Timeframe.M30, Timeframe.M30, 13 },
        { Timeframe.H1, Timeframe.H1, 1 },
        { Timeframe.H1, Timeframe.H1, 5 },
        { Timeframe.H1, Timeframe.H1, 6 },
        { Timeframe.H1, Timeframe.H1, 7 },
        { Timeframe.M5, Timeframe.M1, 4 },
        { Timeframe.M5, Timeframe.M1, 5 },
        { Timeframe.M15, Timeframe.M1, 14 },
        { Timeframe.M15, Timeframe.M1, 15 },
        { Timeframe.M15, Timeframe.M1, 30 },
        { Timeframe.M10, Timeframe.M5, 1 },
        { Timeframe.M10, Timeframe.M5, 2 },
        { Timeframe.M15, Timeframe.M10, 1 },
        { Timeframe.M15, Timeframe.M10, 2 },
        { Timeframe.M30, Timeframe.M10, 2 },
        { Timeframe.M30, Timeframe.M10, 3 },
        { Timeframe.M30, Timeframe.M15, 1 },
        { Timeframe.M30, Timeframe.M15, 2 },
        { Timeframe.M5, Timeframe.M30, 1 },
        { Timeframe.M15, Timeframe.H1, 1 },
        { Timeframe.M15, Timeframe.H1, 7 },
        { Timeframe.M5, Timeframe.M5, 80 },
        { Timeframe.D1, Timeframe.M5, 80 },
        { Timeframe.D1, Timeframe.M5, 74 },
        { Timeframe.D1, Timeframe.H1, 6 },
        { Timeframe.D1, Timeframe.H1, 7 },
        { Timeframe.D1, Timeframe.M1, 100 },
    };

    private static string Reference(Timeframe feature, Timeframe evaluation, string name) => feature == evaluation ? name : "tf(\"" + feature.ToCode() + "\")." + name;

    /// <summary>How many bars of timeframe <paramref name="minutes"/> have closed <paramref name="elapsed"/> minutes after the open: whole bars, and at the close of the session the last short one as well.</summary>
    private static int BarsClosed(int elapsed, int minutes) => elapsed == SessionMinutes ? (SessionMinutes + minutes - 1) / minutes : elapsed / minutes;

    /// <summary>The steps of the second session at which the strategy's timeframe closes a bar, as minutes after the open (the bar of the step closes at the end of its minute).</summary>
    private static List<(int Elapsed, Truth Truth)> Run(Timeframe evaluation, string source, Bar[] bars)
    {
        var rig = Rig.Of(evaluation, source, extra: [Specs.Close(evaluation)]);
        var evaluations = new List<(int, Truth)>();
        for (var i = 0; i < bars.Length; i++)
        {
            var truth = rig.Step(bars[i])[0];
            if (i >= SessionMinutes && rig.Frame.BarClosed(evaluation))
            {
                evaluations.Add((i - SessionMinutes + 1, truth));
            }
        }

        return evaluations;
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void A_lagged_read_of_a_session_feature_is_unknown_exactly_when_the_lane_has_no_such_bar_in_the_session_and_the_lint_warns_when_it_never_will_be_known(Timeframe evaluation, Timeframe feature, int lag)
    {
        var bars = Series.Sessions(seed: 7, sessions: 2);
        bars[0].Open.ShouldNotBe(bars[SessionMinutes].Open, "the probe must differ between the sessions, so that a read of the other session would be true");
        var open = Reference(feature, evaluation, "today_open");
        var source = open + "[" + lag + "] != " + open;

        var evaluations = Run(evaluation, source, bars);

        evaluations.Count.ShouldBeGreaterThan(0);
        foreach (var (elapsed, truth) in evaluations)
        {
            var barsOfSession = BarsClosed(elapsed, feature.Minutes());
            var expected = barsOfSession == 0 || lag >= barsOfSession ? Truth.Unknown : Truth.False;

            truth.ShouldBe(expected, $"{source} on {evaluation.ToCode()}, {elapsed} minutes after the open: the {feature.ToCode()} lane has closed {barsOfSession} bars of the session");
        }

        // the lint: the read is never known exactly when it is unknown at the last evaluation of the session, that is when the lane holds no more than L bars of it
        var warned = Compiling.Of(source, evaluation).Problems.Count(p => p.Code == LintCodes.LagAcrossSession);
        var neverKnown = lag * feature.Minutes() >= SessionMinutes;
        warned.ShouldBe(neverKnown ? 1 : 0, source + " on " + evaluation.ToCode());
        (evaluations[^1].Truth == Truth.Unknown).ShouldBe(neverKnown, $"{source}: the last evaluation of the session has {BarsClosed(SessionMinutes, feature.Minutes())} bars of the lane");
    }

    public static TheoryData<Timeframe, Timeframe> PendingShapes => new()
    {
        { Timeframe.M5, Timeframe.M30 },
        { Timeframe.M15, Timeframe.H1 },
        { Timeframe.M5, Timeframe.H1 },
        { Timeframe.M10, Timeframe.M30 },
        { Timeframe.M15, Timeframe.M30 },
        { Timeframe.M1, Timeframe.H1 },
        { Timeframe.M1, Timeframe.M5 },
        { Timeframe.M5, Timeframe.M10 },
        { Timeframe.M5, Timeframe.M15 },
        { Timeframe.M5, Timeframe.M5 },
        { Timeframe.D1, Timeframe.H1 },
    };

    [Theory]
    [MemberData(nameof(PendingShapes))]
    public void A_session_feature_on_a_coarser_lane_is_unknown_at_lag_zero_until_that_lane_has_closed_its_first_bar_of_the_date(Timeframe evaluation, Timeframe feature)
    {
        // The strategy evaluates every E minutes and reads the lane of T minutes; the lane has closed ⌊m / T⌋ bars of the date at the evaluation m minutes after the open, so the read is unknown while that is 0.
        // The old frame showed the previous session's value here (and, for prev_day_*, the value of two sessions ago).
        var bars = Series.Sessions(seed: 7, sessions: 3);
        foreach (var name in new[] { "today_open", "prev_day_close", "vwap", "gap_pct" })
        {
            var read = Reference(feature, evaluation, name);
            var rig = Rig.Of(evaluation, read + " > -1000000", extra: [Specs.Close(evaluation)]);
            var seen = 0;

            for (var i = 0; i < bars.Length; i++)
            {
                var truth = rig.Step(bars[i])[0];
                if (i < 2 * SessionMinutes || !rig.Frame.BarClosed(evaluation))
                {
                    continue;
                }

                var elapsed = i - 2 * SessionMinutes + 1;
                var expected = BarsClosed(elapsed, feature.Minutes()) == 0 ? Truth.Unknown : Truth.True;
                truth.ShouldBe(expected, $"{read} on {evaluation.ToCode()}, {elapsed} minutes after the open of the third session");
                seen++;
            }

            seen.ShouldBeGreaterThan(0);
        }
    }
}
