using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Tests.Support;
using XDrishti.Strategies.Tests.Support.Oracle;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The reference for the per-session features (<see cref="SessionReference"/>) is checked against answers worked out by hand from the definition (ADR 0035) before the oracle uses it to judge the frame: flat bars of
/// volume 10, so the vwap of a session is the running mean of its closes, and the opening of a session is the first close. The definition: the feature <i>k</i> bars ago is known when <i>k</i> is below the number of bars
/// the feature's timeframe has closed in the date of the last accepted minute, and unknown otherwise.
/// </summary>
public sealed class SessionReferenceSelfTests
{
    private static Leaf Session(string name, Timeframe frame, int lag = 0)
    {
        var text = name.Replace("prev_day_", "prev_day.", StringComparison.Ordinal).Replace("today_", "today.", StringComparison.Ordinal);
        return new Leaf(new Feature(text, name, true, new FeatureSpec(name)), frame, frame != Timeframe.M1, lag);
    }

    private static Cmp Positive(Leaf leaf) => new Cmp(">", leaf, new Lit("0"));

    private static char Letter(Tri value) => value switch { Tri.True => 'T', Tri.False => 'F', _ => 'U' };

    /// <summary>The answers of the model after each bar, the interpreter reading the per-session leaves from a reference fed the same bars.</summary>
    private static string Run(Cmp model, IEnumerable<Bar> bars, bool runOn = false, params FeatureSpec[] extra)
    {
        var leaf = (Leaf)model.Left;
        var specs = new List<FeatureSpec> { leaf.Spec, Specs.Close(Timeframe.M1) };
        specs.AddRange(extra);
        var frame = Frames.Of(new FrameOptions { HistoryDepth = 8 }, [.. specs.Distinct()]);
        var sessions = SessionReference.For(frame, runOn: runOn);
        var interpreter = new Interpreter(frame, sessions);
        return string.Concat(bars.Select(bar =>
        {
            frame.Step(bar);
            sessions.Feed(bar);
            return Letter(interpreter.Evaluate(model));
        }));
    }

    /// <summary>Three sessions of three bars: closes 100 98 96 | 122 121 119 | 90 92 94 (vwap 100 99 98 | 122 121.5 362/3 | 90 91 92).</summary>
    private static Bar[] ThreeSessions()
    {
        double[] closes = [100, 98, 96, 122, 121, 119, 90, 92, 94];
        DateOnly[] days = [Clock.Mon, Clock.Tue, Clock.Wed];
        return [.. closes.Select((c, i) => Clock.Close(days[i / 3], Clock.Open + i % 3, c))];
    }

    private static IEnumerable<Bar> TwoThirtyMinuteSessions() =>
        new[] { (Clock.Mon, 100.0), (Clock.Tue, 200.0) }.SelectMany(d => Enumerable.Range(0, 30).Select(i => Clock.Close(d.Item1, Clock.Open + i, d.Item2 + i)));

    [Fact]
    public void A_one_bar_lag_is_unknown_at_the_first_bar_of_every_session_and_known_after()
    {
        // vwap[1] > 0: nothing before the first bar of a session, so bar 1 U, bar 2 T, bar 3 T; again at the first bar of Tuesday (U) and of Wednesday (U)
        Run(Positive(Session("vwap", Timeframe.M1, 1)), ThreeSessions()).ShouldBe("UTTUTTUTT");
    }

    [Fact]
    public void A_two_bar_lag_is_unknown_for_the_first_two_bars_of_every_session()
    {
        Run(Positive(Session("vwap", Timeframe.M1, 2)), ThreeSessions()).ShouldBe("UUTUUTUUT");
    }

    [Fact]
    public void The_value_now_is_the_sessions_and_the_values_before_it_are_the_sessions_too()
    {
        var sessions = new SessionReference([new FeatureSpec("vwap")]);
        var bars = ThreeSessions();
        for (var i = 0; i < 6; i++)
        {
            sessions.Feed(bars[i]);
        }

        // after Tuesday's third bar: 362/3, 121.5, 122, and nothing of Monday behind them
        sessions.Read(new FeatureSpec("vwap"), 0).ShouldBe(362.0 / 3, 1e-9);
        sessions.Read(new FeatureSpec("vwap"), 1).ShouldBe(121.5, 1e-9);
        sessions.Read(new FeatureSpec("vwap"), 2).ShouldBe(122.0, 1e-9);
        IsUnknown(sessions.Read(new FeatureSpec("vwap"), 3));
    }

    [Fact]
    public void The_run_on_control_reads_by_position_as_the_frame_did_before_the_rule()
    {
        // the same condition with the mask off: Tuesday's first bar sees Monday's last vwap (98) one bar back, and so does Wednesday's: only the very first bar has nothing behind it
        Run(Positive(Session("vwap", Timeframe.M1, 1)), ThreeSessions(), runOn: true).ShouldBe("UTTTTTTTT");
        Run(Positive(Session("vwap", Timeframe.M1, 2)), ThreeSessions(), runOn: true).ShouldBe("UUTTTTTTT");
    }

    [Fact]
    public void A_coarser_lane_is_unknown_until_it_closes_its_first_bar_of_the_date()
    {
        // 15m today_open > 0 over two sessions of 30 minutes: the first 15-minute bar closes at minute 14 of each session
        var first = new string('U', 14) + new string('T', 16);
        Run(Positive(Session("today_open", Timeframe.M15)), TwoThirtyMinuteSessions()).ShouldBe(first + first);
    }

    [Fact]
    public void A_coarser_lane_knows_one_bar_back_only_after_its_second_bar_of_the_date()
    {
        // lag 1 needs two bars of the date: the second closes at minute 29
        var day = new string('U', 29) + "T";
        Run(Positive(Session("today_open", Timeframe.M15, 1)), TwoThirtyMinuteSessions()).ShouldBe(day + day);
    }

    [Fact]
    public void The_previous_days_value_is_unknown_on_the_first_session_and_on_a_pending_lane_of_the_second()
    {
        // prev_day_close on 15m: the first session has no previous one (30 U); the second is pending for 14 minutes and known from the first 15-minute bar
        Run(Positive(Session("prev_day_close", Timeframe.M15)), TwoThirtyMinuteSessions()).ShouldBe(new string('U', 30) + new string('U', 14) + new string('T', 16));
    }

    [Fact]
    public void A_bucket_that_closes_late_and_the_dates_own_bucket_in_one_step_leave_a_session_of_one_bar()
    {
        // Monday without 15:29: its 15:15 bucket (close 110) is closed by Tuesday's first bar, here the 09:29 minute, which also closes Tuesday's own 09:15 bucket
        var specs = new[] { new FeatureSpec("vwap", Timeframe.M15), new FeatureSpec("today_open", Timeframe.M15), new FeatureSpec("prev_day_close", Timeframe.M15) };
        var sessions = new SessionReference(specs);
        for (var m = Clock.At(15, 0); m < Clock.At(15, 29); m++)
        {
            sessions.Feed(Clock.Close(Clock.Mon, m, m < Clock.At(15, 15) ? 100 : 110));
        }

        sessions.Feed(Clock.Close(Clock.Tue, Clock.Open + 14, 120));

        sessions.Read(specs[0], 0).ShouldBe(120.0, 1e-9);
        IsUnknown(sessions.Read(specs[0], 1), "the late bucket of Monday is not a bar of Tuesday");
        sessions.Read(specs[1], 0).ShouldBe(120.0, 1e-9);
        IsUnknown(sessions.Read(specs[1], 1));
        sessions.Read(specs[2], 0).ShouldBe(110.0, 1e-9);
        IsUnknown(sessions.Read(specs[2], 1));
    }

    [Fact]
    public void A_late_bucket_alone_leaves_the_new_date_unknown_at_every_lag()
    {
        var specs = new[] { new FeatureSpec("vwap", Timeframe.M15) };
        var sessions = new SessionReference(specs);
        for (var m = Clock.At(15, 0); m < Clock.At(15, 29); m++)
        {
            sessions.Feed(Clock.Close(Clock.Mon, m, m < Clock.At(15, 15) ? 100 : 110));
        }

        sessions.Feed(Clock.Close(Clock.Tue, Clock.Open, 120));

        IsUnknown(sessions.Read(specs[0], 0), "Tuesday has no closed 15-minute bar yet");
        IsUnknown(sessions.Read(specs[0], 1));
        sessions.Masked.Pending.ShouldBe(2, "both reads would have been the old date's values by position");
    }

    [Fact]
    public void A_bar_at_or_before_the_last_one_changes_nothing()
    {
        var clean = new SessionReference([new FeatureSpec("vwap")]);
        var dirty = new SessionReference([new FeatureSpec("vwap")]);
        var bars = ThreeSessions();
        foreach (var bar in bars.Take(4))
        {
            clean.Feed(bar);
            dirty.Feed(bar);
        }

        dirty.Feed(Clock.Close(Clock.Tue, Clock.Open, 500));
        dirty.Feed(bars[1]);
        foreach (var bar in bars.Skip(4))
        {
            clean.Feed(bar);
            dirty.Feed(bar);
        }

        for (var lag = 0; lag < 4; lag++)
        {
            Tolerance(clean.Read(new FeatureSpec("vwap"), lag), dirty.Read(new FeatureSpec("vwap"), lag)).ShouldBeTrue($"lag {lag}");
        }
    }

    [Fact]
    public void A_reset_forgets_everything()
    {
        var sessions = new SessionReference([new FeatureSpec("vwap")]);
        foreach (var bar in ThreeSessions().Take(5))
        {
            sessions.Feed(bar);
        }

        sessions.Reset();

        IsUnknown(sessions.Read(new FeatureSpec("vwap"), 0));
        sessions.Feed(Clock.Close(Clock.Mon, Clock.Open, 50));
        sessions.Read(new FeatureSpec("vwap"), 0).ShouldBe(50.0, 1e-9);
        IsUnknown(sessions.Read(new FeatureSpec("vwap"), 1));
    }

    private static void IsUnknown(double value, string? because = null) => double.IsNaN(value).ShouldBeTrue($"expected unknown (NaN) but was {value:R}" + (because is null ? string.Empty : " (" + because + ")"));

    private static bool Tolerance(double a, double b) => double.IsNaN(a) ? double.IsNaN(b) : Math.Abs(a - b) < 1e-9;

    [Fact]
    public void Only_the_twelve_names_on_a_timeframe_with_minutes_are_covered()
    {
        foreach (var name in SessionReference.Names)
        {
            foreach (var timeframe in Enum.GetValues<Timeframe>())
            {
                SessionReference.Covers(new FeatureSpec(name, timeframe)).ShouldBe(timeframe.Minutes() > 0, name + " on " + timeframe.ToCode());
            }
        }

        SessionReference.Covers(new FeatureSpec("close", Timeframe.M5)).ShouldBeFalse();
        SessionReference.Covers(new FeatureSpec("ema", Timeframe.M1, PriceSource.Close, 20)).ShouldBeFalse();
        SessionReference.Covers(new FeatureSpec("day_of_week")).ShouldBeFalse();
    }

    [Fact]
    public void The_list_of_the_reference_is_the_per_session_names_of_the_catalog()
    {
        SessionReference.Names.Distinct().Count().ShouldBe(12);
        SessionReference.Names.ShouldBe(IndicatorCatalog.All.Where(i => i.SessionBehaviour == SessionBehaviour.PerSession).Select(i => i.Name), ignoreOrder: true);
    }

    [Fact]
    public void A_feature_the_reference_was_not_given_is_an_error_not_an_unknown()
    {
        var sessions = new SessionReference([new FeatureSpec("vwap")]);

        Should.Throw<InvalidOperationException>(() => sessions.Read(new FeatureSpec("today_open"), 0));
    }

    [Fact]
    public void An_interpreter_built_without_a_reference_throws_when_it_meets_a_per_session_leaf()
    {
        var frame = Frames.Of(new FeatureSpec("vwap"), Specs.Close(Timeframe.M1));
        frame.Step(Clock.Close(Clock.Mon, Clock.Open, 100));
        var model = Positive(Session("vwap", Timeframe.M1));

        Should.Throw<InvalidOperationException>(() => new Interpreter(frame).Evaluate(model)).Message.ShouldContain("SessionReference");
        Should.Throw<InvalidOperationException>(() => new BoundTreeInterpreter(frame).Evaluate(Compiling.Condition("vwap > 0", Timeframe.M1).Tree)).Message.ShouldContain("SessionReference");
        new Interpreter(frame).Evaluate(Positive(new Leaf(new Feature("close", "close", true, new FeatureSpec("close")), Timeframe.M1, false, 0))).ShouldBe(Tri.True);
    }

    [Fact]
    public void A_daily_leaf_is_read_from_the_frame_by_position_the_reference_does_not_cover_it()
    {
        // two daily bars (09:15 and 15:29 each): today_open on 1D is 100 after Monday's close and 120 after Tuesday's, with Monday's one bar back
        var frame = Frames.Of(new FrameOptions { HistoryDepth = 4 }, new FeatureSpec("today_open", Timeframe.D1), Specs.Close(Timeframe.M1));
        var interpreter = new Interpreter(frame);
        foreach (var (date, open, close) in new[] { (Clock.Mon, 100.0, 105.0), (Clock.Tue, 120.0, 121.0) })
        {
            frame.Step(Clock.Bar(date, Clock.Open, open, open, open, open));
            frame.Step(Clock.Close(date, Clock.At(15, 29), close));
        }

        var lagged = new Leaf(new Feature("today.open", "today_open", true, new FeatureSpec("today_open")), Timeframe.D1, true, 1);

        interpreter.Evaluate(new Cmp("==", lagged, new Lit("100"))).ShouldBe(Tri.True, "Monday's open, one daily bar back, is not cleared on 1D");
    }
}
