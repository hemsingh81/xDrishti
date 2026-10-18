using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>
/// Criteria 22 to 24: the first evaluations of a session (ADR 0035) through a list, and the entry window against the clock of the step, including a bar that closes late. The tables are worked out by hand from the
/// definitions.
/// </summary>
public sealed class SessionAndWindowTests
{
    // ---- criterion 22: the first evaluations of a session ---------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 5-minute bars each made of five flat 1-minute bars (open = high = low = close, volume 10 per minute). Monday closes 100, 98, 96; Tuesday 122, 121, 119, 130. The vwap of the closed bars of the session is the running
    /// mean of the closes (equal volumes): 100, 99, 98 | 122, 121.5, 120.667, 123.
    /// </summary>
    private static List<(Bar Bar, bool Last)> TwoSessions()
    {
        var bars = new List<(Bar, bool)>();
        void Session(DateOnly date, params double[] closes)
        {
            for (var b = 0; b < closes.Length; b++)
            {
                for (var m = 0; m < 5; m++)
                {
                    bars.Add((Clock.Close(date, Clock.Open + b * 5 + m, closes[b]), m == 4));
                }
            }
        }

        Session(Clock.Mon, 100, 98, 96);
        Session(Clock.Tue, 122, 121, 119, 130);
        return bars;
    }

    private static StrategyDefinition FiveMinute(ConditionList trigger) => Defs.Basic().WithBuy(b => b with { Trigger = trigger }) with { Timeframes = [Timeframe.M5] };

    /// <summary>Steps the series and returns the evaluation at each closed bar of 5m.</summary>
    private static List<SideEvaluation> ClosedBars(ConditionList trigger, out List<string> items)
    {
        var rig = SideRig.Of(Located.Ok(FiveMinute(trigger), Timeframe.M5));
        var evaluations = new List<SideEvaluation>();
        items = [];
        foreach (var (bar, _) in TwoSessions())
        {
            var evaluation = rig.Step(bar);
            if (rig.Frame.BarClosed(Timeframe.M5))
            {
                evaluations.Add(evaluation);
                items.Add(rig.Word);
            }
        }

        return evaluations;
    }

    [Fact]
    public void Crosses_above_the_vwap_is_unknown_on_the_first_closed_bar_of_a_session_and_true_on_the_fourth_bar_of_tuesday()
    {
        var evaluations = ClosedBars(Defs.All("crosses_above(close, vwap)"), out var words);

        string.Concat(words).ShouldBe("UFFUFFT");
        evaluations.Select(e => e.Trigger).ShouldBe([Truth.Unknown, Truth.False, Truth.False, Truth.Unknown, Truth.False, Truth.False, Truth.True]);
        evaluations[3].Fires.ShouldBeFalse("on the first closed bar of Tuesday the list is unknown, not false, and nothing fires");
        evaluations[6].Fires.ShouldBeTrue();
        evaluations.Count(e => e.Fires).ShouldBe(1);
    }

    [Fact]
    public void An_any_list_with_a_known_true_item_is_true_on_the_first_tuesday_bar_although_the_crossing_is_unknown()
    {
        var evaluations = ClosedBars(Defs.Any("crosses_above(close, vwap)", "close > 100"), out _);

        evaluations[3].Trigger.ShouldBe(Truth.True, "122 > 100, and unknown or true is true");
        evaluations[3].Fires.ShouldBeTrue();
        evaluations[0].Trigger.ShouldBe(Truth.Unknown, "Monday's first bar: the crossing is unknown and 100 > 100 is false");
    }

    [Fact]
    public void The_negation_of_a_crossing_is_unknown_where_the_crossing_is_so_it_cannot_fire_on_the_first_bar_of_a_session()
    {
        var evaluations = ClosedBars(Defs.All("not crosses_below(close, vwap)"), out var words);

        // bar 2: 98 < 99 and the close before (100) is not below the vwap before (100): a crossing below, so its negation is false; bar 5 is the same on Tuesday (121 < 121.5, 122 >= 122)
        string.Concat(words).ShouldBe("UFTUFTT");
        evaluations[3].Trigger.ShouldBe(Truth.Unknown);
        evaluations[3].Fires.ShouldBeFalse();
    }

    // ---- criterion 23: the window ---------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static SideRig Window(TimeOnly? first, TimeOnly? last)
    {
        var definition = Defs.Basic() with { TimeRules = new TimeRules(first, last, new TimeOnly(15, 15), null), Timeframes = [Timeframe.M5] };
        return SideRig.Of(Located.Ok(definition, Timeframe.M5));
    }

    /// <summary>Feeds the minutes of Monday from 09:15 up to the one before the given clock time (so that <c>time</c> at the step is the given clock time) and asks for the window.</summary>
    private static bool OpenAt(SideRig rig, int hour, int minute)
    {
        var target = Clock.At(hour, minute);
        for (var m = Clock.Open; m < target; m++)
        {
            rig.Frame.Step(Clock.Close(Clock.Mon, m, 100));
        }

        return rig.Bound.InEntryWindow(rig.Frame);
    }

    [Theory]
    [InlineData(9, 25, false)]
    [InlineData(9, 30, true)]
    [InlineData(11, 30, true)]
    [InlineData(11, 35, false)]
    [InlineData(9, 20, false)]
    [InlineData(12, 0, false)]
    public void The_window_is_the_inclusive_first_entry_to_last_entry_of_the_close_time_of_the_step(int hour, int minute, bool expected)
    {
        var rig = Window(new TimeOnly(9, 30), new TimeOnly(11, 30));

        OpenAt(rig, hour, minute).ShouldBe(expected, $"{hour:00}:{minute:00}");
    }

    [Fact]
    public void The_window_is_closed_before_the_first_step()
    {
        var rig = Window(new TimeOnly(9, 30), new TimeOnly(11, 30));
        var open = Window(null, null);

        rig.Bound.InEntryWindow(rig.Frame).ShouldBeFalse();
        open.Bound.InEntryWindow(open.Frame).ShouldBeFalse("both ends open is still closed before the first step");
    }

    [Fact]
    public void A_missing_first_entry_opens_the_window_from_the_start_and_a_missing_last_entry_keeps_it_open_to_the_end()
    {
        OpenAt(Window(null, new TimeOnly(11, 30)), 9, 20).ShouldBeTrue("first_entry absent: true at 09:20");
        OpenAt(Window(new TimeOnly(9, 30), null), 15, 10).ShouldBeTrue("last_entry absent: true at 15:10");
        OpenAt(Window(null, null), 9, 16).ShouldBeTrue("both absent: true after the first step");
        OpenAt(Window(null, null), 15, 10).ShouldBeTrue();
        OpenAt(Window(new TimeOnly(9, 30), null), 9, 25).ShouldBeFalse();
        OpenAt(Window(null, new TimeOnly(11, 30)), 11, 35).ShouldBeFalse();
    }

    [Fact]
    public void The_window_reads_the_clock_through_the_same_function_as_the_time_keyword()
    {
        var timeIsAtLeast = Window(new TimeOnly(9, 30), null);
        var condition = SideRig.Of(Located.Ok(Defs.Basic().WithBuy(b => b with { Trigger = Defs.All("time >= \"09:30\"") }) with { Timeframes = [Timeframe.M5] }, Timeframe.M5));

        for (var m = Clock.Open; m < Clock.At(9, 45); m++)
        {
            var bar = Clock.Close(Clock.Mon, m, 100);
            timeIsAtLeast.Frame.Step(bar);
            var evaluation = condition.Step(bar);
            (evaluation.Trigger == Truth.True).ShouldBe(timeIsAtLeast.Bound.InEntryWindow(timeIsAtLeast.Frame), "minute " + m);
        }
    }

    // ---- criterion 24: a late bar -----------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static SideRig WithMissingMinute(TimeOnly? first, TimeOnly? last, out bool closedAtThatStep)
    {
        var rig = Window(first, last);
        for (var m = Clock.Open; m <= Clock.At(9, 28); m++)
        {
            rig.Frame.Step(Clock.Close(Clock.Mon, m, 100));
        }

        rig.Frame.Step(Clock.Close(Clock.Mon, Clock.At(9, 30), 100));
        closedAtThatStep = rig.Frame.BarClosed(Timeframe.M5);
        return rig;
    }

    [Fact]
    public void A_bar_that_closes_late_is_judged_by_the_clock_of_the_step_the_0925_bucket_closes_at_the_0930_minute_where_time_is_0931()
    {
        var rig = WithMissingMinute(new TimeOnly(9, 30), new TimeOnly(11, 30), out var closed);

        closed.ShouldBeTrue("the 09:25 bucket closes at the step of the 09:30 minute, the 09:29 minute being missing");
        rig.Bound.InEntryWindow(rig.Frame).ShouldBeTrue("time at that step is 09:31");
    }

    [Fact]
    public void With_last_entry_at_0930_the_window_is_closed_at_that_step_although_the_bar_would_have_closed_at_0930()
    {
        var rig = WithMissingMinute(new TimeOnly(9, 30), new TimeOnly(9, 30), out var closed);

        closed.ShouldBeTrue();
        rig.Bound.InEntryWindow(rig.Frame).ShouldBeFalse();
    }

    [Fact]
    public void Without_a_window_the_first_step_of_the_next_date_is_open_for_a_bucket_of_the_date_before_that_this_minute_closed_late_because_the_side_cannot_tell_it_from_a_bar_of_the_day()
    {
        var rig = Window(null, null);
        for (var m = Clock.Open; m <= Clock.At(15, 28); m++)
        {
            rig.Frame.Step(Clock.Close(Clock.Mon, m, 100));
        }

        rig.Frame.Step(Clock.Close(Clock.Tue, Clock.Open, 100));

        rig.Frame.BarClosed(Timeframe.M5).ShouldBeTrue("the 15:25 bucket of Monday closes at the 09:15 minute of Tuesday, the 15:29 minute being missing");
        rig.Bound.InEntryWindow(rig.Frame).ShouldBeTrue("time is 09:16; the scanner guards a late bar of the date before (P3-03), the library may not read TryLastClosed or ClosedBarsAt");
    }
}
