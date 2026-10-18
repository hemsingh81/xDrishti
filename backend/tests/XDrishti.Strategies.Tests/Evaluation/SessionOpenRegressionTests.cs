using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The measurement of the final review of the rules language can never return (ADR 0034, "Lagged reads are by position, not by session"; ADR 0035): on a seeded random walk of 500 sessions
/// <c>crosses_above(close, vwap)</c> was true on the first bar of about a quarter of the sessions, against a few percent at every other bar, because the bar before the first one was the last bar of the session before.
/// Now none of the crossing forms is ever true at the first evaluation of a session, each is true in-session at a rate of a few percent (so the test is not vacuous), and the level form that says "through yesterday's
/// high" is true at the first evaluation of some sessions. Evaluations are the closes of the strategy's timeframe; the first of a session is the first of its India date.
/// </summary>
public sealed class SessionOpenRegressionTests
{
    private const int Sessions = 500;

    private static readonly string[] Crossings =
    [
        "crosses_above(close, vwap)",
        "crosses_above(close, today_open)",
        "rising(vwap, 1)",
        "close > vwap and close[1] <= vwap[1]",
        "crosses_above(close, prev_day.high)",
    ];

    private const string Level = "close > prev_day.high and close[1] <= prev_day.high";

    public static TheoryData<Timeframe> Timeframes { get; } = [Timeframe.M1, Timeframe.M15];

    /// <summary>For each condition: the truths at the first evaluation of every session and at all the other evaluations.</summary>
    private static (Truth[][] First, Truth[][] Others) Run(Timeframe timeframe, string[] sources)
    {
        var bars = Series.Sessions(seed: 11, sessions: Sessions);
        var rig = Rig.Of(timeframe, sources);
        var first = sources.Select(_ => new List<Truth>()).ToArray();
        var others = sources.Select(_ => new List<Truth>()).ToArray();
        DateOnly? lastDate = null;
        foreach (var bar in bars)
        {
            var truths = rig.Step(bar);
            if (!rig.Frame.BarClosed(timeframe))
            {
                continue;
            }

            var date = SessionClock.DateOf(bar.EpochSeconds);
            var isFirst = date != lastDate;
            lastDate = date;
            for (var i = 0; i < sources.Length; i++)
            {
                (isFirst ? first[i] : others[i]).Add(truths[i]);
            }
        }

        return ([.. first.Select(l => l.ToArray())], [.. others.Select(l => l.ToArray())]);
    }

    [Theory]
    [MemberData(nameof(Timeframes))]
    public void The_crossing_forms_are_never_true_at_the_first_evaluation_of_a_session_and_are_true_now_and_then_inside_one(Timeframe timeframe)
    {
        var (first, others) = Run(timeframe, Crossings);

        for (var i = 0; i < Crossings.Length; i++)
        {
            first[i].Length.ShouldBe(Sessions, Crossings[i]);
            first[i].Count(t => t == Truth.True).ShouldBe(0, $"{Crossings[i]} on {timeframe.ToCode()} was true at the first evaluation of a session");
            var rate = others[i].Count(t => t == Truth.True) / (double)others[i].Length;
            rate.ShouldBeGreaterThan(0.0, $"{Crossings[i]} on {timeframe.ToCode()} is never true inside a session: the control is vacuous");

            // a crossing is true at a few percent of the bars; rising(vwap, 1) is not a crossing, the vwap of a random walk rises about as often as it falls
            rate.ShouldBeLessThan(i == 2 ? 0.75 : 0.25, $"{Crossings[i]} on {timeframe.ToCode()}");
        }
    }

    [Theory]
    [MemberData(nameof(Timeframes))]
    public void The_first_evaluation_of_a_session_is_unknown_for_the_crossings_and_rising_not_false(Timeframe timeframe)
    {
        var (first, _) = Run(timeframe, Crossings);

        // crosses_* and rising are unknown whenever an operand a bar back does not exist: at every first evaluation, for these four; the written-out form is unknown or false (false when the first condition already fails)
        foreach (var i in new[] { 0, 1, 2, 4 })
        {
            first[i].ShouldAllBe(t => t == Truth.Unknown, $"{Crossings[i]} on {timeframe.ToCode()}");
        }

        first[3].ShouldAllBe(t => t != Truth.True, $"{Crossings[3]} on {timeframe.ToCode()}");
        first[3].Count(t => t == Truth.Unknown).ShouldBeGreaterThan(0, "close > vwap holds at the first bar of some sessions");
    }

    [Theory]
    [MemberData(nameof(Timeframes))]
    public void The_level_form_is_true_at_the_first_evaluation_of_some_sessions_the_gaps_through_the_previous_high(Timeframe timeframe)
    {
        var (first, others) = Run(timeframe, [Level]);

        first[0].Count(t => t == Truth.True).ShouldBeGreaterThan(0, $"the level form on {timeframe.ToCode()}");
        first[0].Count(t => t == Truth.True).ShouldBeLessThan(Sessions / 2);
        others[0].ShouldContain(Truth.True);
    }
}
