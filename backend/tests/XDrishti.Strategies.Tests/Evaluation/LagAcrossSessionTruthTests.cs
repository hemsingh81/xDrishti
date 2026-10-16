using System.Globalization;
using System.Text.RegularExpressions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The arithmetic of <c>lag_across_session</c> checked against the real frame: whether the lint warns, and the clock time it asks for, are compared with what <see cref="FeatureFrame.At"/> returns at every evaluation of
/// the second of two seeded sessions. <c>today_open</c> is the probe: it is constant inside a session and differs between the two, so a read one lag back that is not equal to the value now belongs to the other
/// session. The frame, the sessions and the evaluations (the closes of the strategy's timeframe) come from the frame and not from the lint, which only knows the clock.
/// </summary>
public sealed partial class LagAcrossSessionTruthTests
{
    private const int SessionMinutes = SessionClock.RegularBars;

    /// <summary>The strategy timeframe, the timeframe of the feature and the lag; chosen so that every kind of case occurs: the same timeframe, finer, coarser, the boundary where the lint stops warning, the end of the session.</summary>
    public static TheoryData<Timeframe, Timeframe, int> Shapes => new()
    {
        { Timeframe.M1, Timeframe.M1, 1 },
        { Timeframe.M1, Timeframe.M1, 3 },
        { Timeframe.M5, Timeframe.M5, 1 },
        { Timeframe.M5, Timeframe.M5, 3 },
        { Timeframe.M15, Timeframe.M15, 1 },
        { Timeframe.M15, Timeframe.M15, 2 },
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
    };

    [GeneratedRegex("`and time >= \"(\\d\\d):(\\d\\d)\"`")]
    private static partial Regex GuardLiteral();

    private static string Reference(Timeframe feature, Timeframe evaluation) => feature == evaluation ? "today_open" : "tf(\"" + feature.ToCode() + "\").today_open";

    [Theory]
    [MemberData(nameof(Shapes))]
    public void The_lint_warns_exactly_when_some_evaluation_reads_the_other_session_and_the_time_it_asks_for_clears_every_one(Timeframe evaluation, Timeframe feature, int lag)
    {
        var bars = Series.Sessions(seed: 7, sessions: 2);
        bars[0].Open.ShouldNotBe(bars[SessionMinutes].Open, "the probe must differ between the sessions");
        var open = OpenOf(feature);
        var frame = Frames.Of(new FrameOptions(HistoryDepth: lag + 1), open, Specs.Close(evaluation));
        var handle = frame.HandleOf(open);

        // every evaluation of the second session: minutes after the open at which its bar closes, and whether the value one lag back is the other session's
        var evaluations = new List<(int Minute, bool Crosses)>();
        for (var i = 0; i < bars.Length; i++)
        {
            frame.Step(bars[i]);
            if (i >= SessionMinutes && frame.BarClosed(evaluation))
            {
                evaluations.Add((i - SessionMinutes + 1, frame.At(handle, lag) != frame[handle]));
            }
        }

        evaluations.Count.ShouldBeGreaterThan(0);
        var source = "rising(" + Reference(feature, evaluation) + ", " + lag + ")";
        var warnings = Compiling.Of(source, evaluation).Problems.Where(p => p.Code == LintCodes.LagAcrossSession).ToList();
        var crossing = evaluations.Where(e => e.Crosses).ToList();
        var description = source + " on " + evaluation.ToCode() + ": evaluations that cross are at minute " + string.Join(", ", crossing.Select(e => e.Minute));

        (warnings.Count == 1).ShouldBe(crossing.Count > 0, description);
        if (warnings.Count == 0)
        {
            return;
        }

        var match = GuardLiteral().Match(warnings[0].Message);
        if (!match.Success)
        {
            // no time clears it: even the last evaluation of the session, at its close, reads the other session
            evaluations[^1].Crosses.ShouldBeTrue(description);
            return;
        }

        var minute = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) - SessionClock.OpenMinute;
        evaluations.Where(e => e.Minute >= minute).ShouldAllBe(e => !e.Crosses, description + "; the lint asks for minute " + minute);
        var lastCrossing = crossing[^1].Minute;
        lastCrossing.ShouldBeLessThan(minute, description + "; the lint asks for minute " + minute);
        lastCrossing.ShouldBeGreaterThanOrEqualTo(minute - Math.Max(evaluation.Minutes(), 1), description + "; the lint asks for minute " + minute + ", later than the evaluation after the last crossing");

        // and a condition with that check is not warned about any more
        var guarded = source + " and time >= \"" + match.Groups[1].Value + ":" + match.Groups[2].Value + "\"";
        Compiling.Of(guarded, evaluation).Problems.Where(p => p.Code == LintCodes.LagAcrossSession).ShouldBeEmpty(guarded);
    }

    private static FeatureSpec OpenOf(Timeframe timeframe) => Specs.Of("today_open", timeframe);
}
