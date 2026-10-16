using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// A lagged read is by position, not by session (ADR 0034, Amendment 19): the frame's history runs on across sessions, so on the first bar of a session <c>close[1]</c> and <c>vwap[1]</c> are the last values of the
/// session before. Two sessions of at most four 1-minute bars with the results worked out by hand pin what that does to <c>crosses_above(close, vwap)</c> — the headline example of the language — and show that
/// the <c>time</c> check the warning asks for removes it. The tables are calculated from the definitions, not from the library.
/// </summary>
public sealed class SessionBoundaryExamplesTests
{
    /// <summary>
    /// Monday: three bars that slide under their own vwap (closes 100, 98, 96, volume 10 each, so vwap = the running mean of the closes: 100, 99, 98). Tuesday opens with a gap up: the first bar is
    /// (open 119, high 123, low 117, close 122), typical price (123 + 117 + 122) / 3 = 120.667; then closes 121, 119 and 130.
    /// <code>
    /// bar:     1    2    3  |  4        5        6        7
    /// close:   100  98   96 |  122      121      119      130
    /// vwap:    100  99   98 |  120.667  120.833  120.222  122.667   (Tuesday's mean of the typical prices: (120.667 + 121) / 2, (120.667 + 121 + 119) / 3, (… + 130) / 4)
    /// open of the day: 100  |  119
    /// time at the close of the bar: 09:16 09:17 09:18 | 09:16 09:17 09:18 09:19
    /// </code>
    /// </summary>
    private static Bar[] TwoSessions() =>
    [
        Clock.Bar(Clock.Mon, Clock.Open, 100, 100, 100, 100),
        Clock.Close(Clock.Mon, Clock.Open + 1, 98),
        Clock.Close(Clock.Mon, Clock.Open + 2, 96),
        Clock.Bar(Clock.Tue, Clock.Open, 119, 123, 117, 122),
        Clock.Close(Clock.Tue, Clock.Open + 1, 121),
        Clock.Close(Clock.Tue, Clock.Open + 2, 119),
        Clock.Close(Clock.Tue, Clock.Open + 3, 130),
    ];

    [Fact]
    public void Crosses_above_the_vwap_is_true_on_the_first_bar_of_the_second_session_because_the_previous_bar_is_the_last_one_of_the_first()
    {
        // bar 1: no bar before it, unknown. 2: 98 > 99 no. 3: 96 > 98 no.
        // bar 4: 122 > 120.667 and the bar before (the last of Monday) has close 96 <= its vwap 98: true — a cross of two different sessions, on a bar that has no bar before it in its own session.
        // bar 5: 121 > 120.833, but the previous close 122 is above the previous vwap 120.667: no. 6: 119 > 120.222 no.
        // bar 7: 130 > 122.667 and the previous close 119 <= 120.222: true, a cross inside Tuesday.
        var rig = Rig.Of(Timeframe.M1, "crosses_above(close, vwap)");

        rig.Run(TwoSessions()).ShouldBe("UFFTFFT");
    }

    [Fact]
    public void Rising_vwap_is_true_on_the_first_bar_of_the_second_session_because_the_vwap_starts_again_higher()
    {
        // vwap: 100, 99, 98 | 120.667, 120.833, 120.222, 122.667.
        // bar 1 unknown. 2: 99 > 100 no. 3: 98 > 99 no. 4: 120.667 > 98 yes (the vwap of a new session against the old one). 5: 120.833 > 120.667 yes. 6: 120.222 > 120.833 no. 7: 122.667 > 120.222 yes.
        var rig = Rig.Of(Timeframe.M1, "rising(vwap, 1)");

        rig.Run(TwoSessions()).ShouldBe("UFFTTFT");
    }

    [Fact]
    public void Crosses_above_the_open_of_the_day_reads_the_open_of_the_previous_day_one_bar_back()
    {
        // open of the day: 100 on Monday, 119 on Tuesday. Bar 2: 98 > 100 no. 3: no.
        // Bar 4: 122 > 119 and the previous close 96 <= the previous open of the day 100: true (Monday's close against Monday's open). 5: 121 > 119, previous 122 <= 119 no. 6: 119 > 119 no.
        // Bar 7: 130 > 119 and the previous close 119 <= 119 (equal counts as not above yet): true.
        var rig = Rig.Of(Timeframe.M1, "crosses_above(close, today_open)");

        rig.Run(TwoSessions()).ShouldBe("UFFTFFT");
    }

    [Fact]
    public void A_continuous_feature_runs_on_across_the_sessions_on_purpose()
    {
        // closes 100 98 96 | 122 121 119 130: rising(close, 1) is close > the previous close, whatever session that bar is in (ADR 0031: a moving average at 09:30 uses the previous day's bars).
        var rig = Rig.Of(Timeframe.M1, "rising(close, 1)");

        rig.Run(TwoSessions()).ShouldBe("UFFTFFT");
    }

    [Fact]
    public void A_time_check_that_the_warning_asks_for_removes_the_cross_of_two_sessions_and_keeps_the_real_one()
    {
        // The warning for crosses_above(close, vwap) on a 1m strategy is "time >= 09:17": the first bar of a session closes at 09:16, and a read one bar back is inside the session from the next bar on.
        // time at the close of bars 1 to 7: 09:16 09:17 09:18 | 09:16 09:17 09:18 09:19. The check is false at bars 1 and 4 (false and unknown is false at bar 1), so bar 4 is no longer true; bar 7 stays true.
        var bare = Compiling.Of("crosses_above(close, vwap)", Timeframe.M1);
        var warning = bare.Problems.ShouldHaveSingleItem();
        warning.Code.ShouldBe(LintCodes.LagAcrossSession);
        warning.Message.ShouldContain("`and time >= \"09:17\"`");

        var guarded = Rig.Of(Timeframe.M1, "crosses_above(close, vwap) and time >= \"09:17\"");

        Compiling.Of("crosses_above(close, vwap) and time >= \"09:17\"", Timeframe.M1).Problems.ShouldBeEmpty();
        guarded.Run(TwoSessions()).ShouldBe("FFFFFFT");
    }

    [Fact]
    public void A_check_one_minute_too_early_leaves_the_cross_of_two_sessions_and_the_warning_says_so()
    {
        // time >= 09:16 is true at the close of the first bar of each session, which is exactly the bar that reads the session before: the word is the unguarded one (bar 1 stays unknown, true and unknown).
        var early = "crosses_above(close, vwap) and time >= \"09:16\"";

        Compiling.Of(early, Timeframe.M1).Problems.ShouldHaveSingleItem().Code.ShouldBe(LintCodes.LagAcrossSession);
        Rig.Of(Timeframe.M1, early).Run(TwoSessions()).ShouldBe("UFFTFFT");
    }
}
