using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.Model;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// P15-09 criteria 2, 3 and 6 through the real <see cref="Simulator"/>: a swing walk of an event side that holds 20 sessions counted by the calendar (a holiday skipped, a special session counted) and leaves by <c>MAX_HOLD</c>, by stop or by target; the <c>event_exit</c> overlay
/// with its hand cases (it fires at the close of the session whose <c>days_to_results</c> at the start is at most the threshold, it never fires on an unknown number and counts it); and a decision at the close of the reaction session that fills at the next open, never at the announcement.
/// Hand-worked: a filing broadcast on Friday 2026-02-27 at 16:12 reacts on Monday 2026-03-02 (<c>days_since_results == 0</c>); Monday is an up day (open 99, close 100, low 98), the stop is its low (98.00), every later session is flat at 100.05 unless a test says otherwise.
/// </summary>
public sealed class EventHoldSimulatorTests
{
    private static readonly Guid InstrumentId = Scan.Instrument.Id;

    private static readonly DateTimeOffset Broadcast = new(2026, 2, 27, 16, 12, 0, EventRows.Ist);

    private static readonly ResultsFilingTime Filing = new(InstrumentId, EventRows.Period, Broadcast);

    private static ExitParameter Days(int d) => new("days_before_results", [Quantity.Of(d)]);

    private static StrategyDefinition EventSide(int hold, int? eventExitDays = null) =>
        TestStrategies.SwingDaily("days_since_results == 0", hold).WithBuy(b => b with { Overlays = eventExitDays is { } d ? [new OverlaySpec(OverlayKind.EventExit, [Days(d)])] : null });

    private static DateOnly[] Weekdays(DateOnly from, DateOnly to)
    {
        var dates = new List<DateOnly>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                dates.Add(date);
            }
        }

        return [.. dates];
    }

    /// <summary>Monday 2026-03-02 as the up day S1 of the daily fixture, then flat sessions at 100.05 on every given date (a date may carry its own day).</summary>
    private static Bar[] Bars(IEnumerable<DateOnly> sessions, Func<DateOnly, Day?>? special = null, DateOnly? reaction = null)
    {
        var days = new List<Day> { Day.Dense(reaction ?? Mon).Start(99.00).Set("10:00 99.00/100.00/98.00/100.00") };
        days.AddRange(sessions.Where(d => d > (reaction ?? Mon)).Select(d => special?.Invoke(d) ?? Day.Dense(d).Start(100.05)));
        return Day.Join([.. days]);
    }

    private static SimulationResult Run(StrategyDefinition definition, IReadOnlyList<Bar> bars, ResultsDayInputs inputs, TradingCalendar? calendar = null)
    {
        var simulator = Sim.Create(definition, Timeframe.D1, Cand.NoSlippage);
        var result = simulator.Run(Scan.Instrument, bars, Sim.Tick, calendar ?? DefaultWeek(), null, null, null, inputs);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    private static ResultsDayInputs Filed() => ResultsDayInputs.Create(null, [Filing], 5);

    private static ResultsDayInputs FiledWithMeeting(DateOnly meeting, DateTimeOffset? intimated = null, string status = MeetingRow.Scheduled) =>
        ResultsDayInputs.Create(EventCalendarSnapshot.Create([EventRows.Meeting(InstrumentId, meeting, 1, intimated, status)], 5), [Filing], 5);

    private static long At(DateOnly date, string clock) => Day.Epoch(date, Day.Minute(clock));

    // ---- criterion 6: the fill is at the next open ----------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_filing_broadcast_on_friday_evening_is_decided_at_the_monday_close_and_filled_at_the_tuesday_open_never_on_monday()
    {
        var result = Run(EventSide(5), Bars(Weekdays(Mon, new DateOnly(2026, 3, 13))), Filed());

        var trade = result.Trades.ShouldHaveSingleItem();
        result.Outcomes.ShouldHaveSingleItem("days_since_results is 0 on Monday only");
        trade.DecisionEpochSeconds.ShouldBe(At(Mon, "15:30"));
        trade.EntryEpochSeconds.ShouldBe(At(Tue, "09:15"), "the first trade within five minutes of Tuesday's open");
        trade.EntryEpochSeconds.ShouldBeGreaterThan(At(Mon, "15:30"));
        trade.EntryPrice.ShouldBe(100.05, "the open of Tuesday, not the close of Monday");
        trade.TradeDate.ShouldBe(Tue);
        trade.StopPrice.ShouldBe(98.00);
    }

    // ---- criterion 2: the walk to 20 sessions ---------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_hold_of_twenty_sessions_leaves_by_max_hold_at_the_close_of_the_twentieth_session_counted_from_the_fill_session()
    {
        var result = Run(EventSide(20), Bars(Weekdays(Mon, new DateOnly(2026, 4, 3))), Filed());

        var trade = result.Trades.ShouldHaveSingleItem();
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
        // Tuesday 2026-03-03 is session 1: the weekdays of 03-03 to 03-27 are 19, so session 20 is Monday 2026-03-30
        trade.ExitEpochSeconds.ShouldBe(At(new DateOnly(2026, 3, 30), "15:29"));
        trade.ExitPrice.ShouldBe(100.05);
        trade.SessionsHeld.ShouldBe(20);
        result.Walk.ExitsMaxHold.ShouldBe(1);
    }

    [Fact]
    public void Holidays_are_skipped_and_a_special_session_is_counted_by_the_calendar()
    {
        var holiday11 = TradingDay.Holiday(new DateOnly(2026, 3, 11), CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch);
        var holiday25 = TradingDay.Holiday(new DateOnly(2026, 3, 25), CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch);
        var special = TradingDay.Special(Sat, 9 * 60 + 15, 12 * 60 + 30, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch);
        var calendar = new TradingCalendar([holiday11, holiday25, special]);
        var sessions = Weekdays(Mon, new DateOnly(2026, 4, 3)).Where(d => d != holiday11.Id && d != holiday25.Id).Append(Sat).Order().ToArray();

        var result = Run(EventSide(20), Bars(sessions, d => d == Sat ? Day.Dense(d, last: "12:29").Start(100.05) : null), Filed(), calendar);

        var trade = result.Trades.ShouldHaveSingleItem();
        // sessions from 03-03: 19 weekdays to 03-30, plus the special Saturday, minus the two holidays, is 18 by 03-30; the twentieth is Tuesday 2026-03-31
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
        trade.ExitEpochSeconds.ShouldBe(At(new DateOnly(2026, 3, 31), "15:29"));
        trade.SessionsHeld.ShouldBe(20);
    }

    [Fact]
    public void A_long_hold_still_leaves_by_the_stop_when_a_later_session_touches_it()
    {
        // session 8 (Thursday 2026-03-12) dips to 97.00 through the stop 98.00 and the position leaves at the stop
        var result = Run(EventSide(20), Bars(Weekdays(Mon, new DateOnly(2026, 4, 3)), d => d == new DateOnly(2026, 3, 12) ? Day.Dense(d).Start(100.05).Set("11:00 100.05/100.05/97.00/100.05") : null), Filed());

        var trade = result.Trades.ShouldHaveSingleItem();
        trade.ExitReason.ShouldBe(ExitReason.Stop);
        trade.ExitPrice.ShouldBe(98.00);
        trade.ExitEpochSeconds.ShouldBe(At(new DateOnly(2026, 3, 12), "11:00"));
        trade.SessionsHeld.ShouldBe(8);
    }

    [Fact]
    public void A_long_hold_still_leaves_by_the_target_when_a_later_session_reaches_it()
    {
        // risk = 100.05 - 98.00 = 2.05; the target at 2 R is 104.15; session 6 (Tuesday 2026-03-10) trades up to it
        var result = Run(EventSide(20), Bars(Weekdays(Mon, new DateOnly(2026, 4, 3)), d => d == new DateOnly(2026, 3, 10) ? Day.Dense(d).Start(100.05).Set("11:00 100.05/105.00/100.05/104.50") : null), Filed());

        var trade = result.Trades.ShouldHaveSingleItem();
        trade.ExitReason.ShouldBe(ExitReason.Target);
        trade.ExitPrice.ShouldBe(104.15, 1e-9);
        result.Walk.ExitsTarget.ShouldBe(1);
    }

    // ---- criterion 3: the event exit ------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_meeting_one_session_away_at_the_start_of_a_session_exits_at_that_close_when_the_threshold_is_one()
    {
        // the meeting is Wednesday 2026-03-11, intimated on 2026-02-20: days_to_results is 1 at the start of Tuesday 03-10
        var result = Run(EventSide(20, 1), Bars(Weekdays(Mon, new DateOnly(2026, 4, 3))), FiledWithMeeting(new DateOnly(2026, 3, 11)));

        var trade = result.Trades.ShouldHaveSingleItem();
        trade.ExitReason.ShouldBe(ExitReason.EventExit);
        trade.ExitEpochSeconds.ShouldBe(At(new DateOnly(2026, 3, 10), "15:29"));
        trade.ExitPrice.ShouldBe(100.05);
        result.Walk.ExitsEventExit.ShouldBe(1);
        result.Walk.EventExitUnavailable.ShouldBe(0);
    }

    [Fact]
    public void A_meeting_intimated_during_a_session_is_not_seen_before_the_next_session()
    {
        // intimated on Tuesday 2026-03-10 at 11:00 for Wednesday 03-11: unknown at the start of Tuesday (counted, no exit), 0 at the start of Wednesday, which is within one session
        var result = Run(EventSide(20, 1), Bars(Weekdays(Mon, new DateOnly(2026, 4, 3))), FiledWithMeeting(new DateOnly(2026, 3, 11), new DateTimeOffset(2026, 3, 10, 11, 0, 0, EventRows.Ist)));

        var trade = result.Trades.ShouldHaveSingleItem();
        trade.ExitReason.ShouldBe(ExitReason.EventExit);
        trade.ExitEpochSeconds.ShouldBe(At(new DateOnly(2026, 3, 11), "15:29"), "not at the close of Tuesday, the session in which it was intimated");
        result.Walk.EventExitUnavailable.ShouldBe(6, "the closes of 03-03, 03-04, 03-05, 03-06, 03-09 and 03-10: the number was not yet known at the start of any of them");
    }

    [Fact]
    public void With_no_visible_entry_the_position_stays_and_every_session_close_is_counted_as_unavailable()
    {
        // no calendar row at all: days_to_results is unknown on every date. Not "no event" and not "an event": the hold of five sessions leaves by MAX_HOLD at the close of 03-09
        var result = Run(EventSide(5, 1), Bars(Weekdays(Mon, new DateOnly(2026, 3, 20))), Filed());

        var trade = result.Trades.ShouldHaveSingleItem();
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
        trade.ExitEpochSeconds.ShouldBe(At(NextMon, "15:29"));
        result.Walk.ExitsEventExit.ShouldBe(0);
        result.Walk.EventExitUnavailable.ShouldBe(4, "the closes of 03-03, 03-04, 03-05 and 03-06; the fifth session leaves by the longest hold");
        result.Walk.EventExitUnavailableTrades.ShouldBe(1);
    }

    [Fact]
    public void A_cancelled_meeting_is_unavailable_and_never_fires()
    {
        var result = Run(EventSide(5, 10), Bars(Weekdays(Mon, new DateOnly(2026, 3, 20))), FiledWithMeeting(NextMon, status: MeetingRow.Cancelled));

        var trade = result.Trades.ShouldHaveSingleItem();
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
        result.Walk.ExitsEventExit.ShouldBe(0);
        result.Walk.EventExitUnavailable.ShouldBe(4);
    }

    [Fact]
    public void The_longest_hold_wins_when_it_falls_on_the_same_close_as_the_event_exit()
    {
        // the meeting is Tuesday 2026-03-10: days_to_results is 1 at the start of Monday 03-09, the fifth session, which is also the due session of a hold of five
        var result = Run(EventSide(5, 1), Bars(Weekdays(Mon, new DateOnly(2026, 3, 20))), FiledWithMeeting(new DateOnly(2026, 3, 10)));

        var trade = result.Trades.ShouldHaveSingleItem();
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
        trade.ExitEpochSeconds.ShouldBe(At(NextMon, "15:29"));
        result.Walk.ExitsEventExit.ShouldBe(0);
    }

    [Fact]
    public void The_overlay_is_run_by_a_swing_side_and_refused_on_an_intraday_one_and_a_strategy_that_lists_it_needs_the_results_inputs()
    {
        var swing = Simulator.Create(EventSide(5, 1), Timeframe.D1);
        var intraday = Simulator.Create(TestStrategies.OrbBuyOnly().WithBuy(b => b with { Overlays = [new OverlaySpec(OverlayKind.EventExit, [Days(1)])] }), Timeframe.M15);

        swing.Simulator.ShouldNotBeNull();
        swing.Simulator.ReadsResultsDays.ShouldBeTrue();
        intraday.Simulator.ShouldBeNull();
        intraday.Problems.ShouldContain(p => p.Problem.Code == "simulation.unsupported_exit" && p.Path == "buy.overlays[0]");
        var noInputs = Sim.Create(EventSide(5, 1), Timeframe.D1).Run(Scan.Instrument, Bars(Weekdays(Mon, Fri)), Sim.Tick, DefaultWeek());
        noInputs.IsFailure.ShouldBeTrue();
        noInputs.Error.Code.ShouldBe("simulation.filter_not_available");
    }

    // ---- the atr_mult stop ------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_atr_stop_is_the_close_of_the_decision_bar_less_the_multiple_of_the_daily_atr()
    {
        // fifteen flat sessions with a range of 4 (high 102, low 98, close 100) before the reaction session: ATR(14) = 4, so the stop is 100 - 2 x 4 = 92
        var definition = TestStrategies.SwingDaily("days_since_results == 0", 5).WithBuy(b => b with { Stop = new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2), AtrPeriod: Quantity.Of(14)) });
        var history = Weekdays(new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 27)).Select(d => Day.Dense(d).Start(100.00).Set("10:00 100.00/102.00/98.00/100.00"));
        var monday = Day.Dense(Mon).Start(100.00).Set("10:00 100.00/102.00/98.00/100.00");
        var after = Weekdays(Tue, new DateOnly(2026, 3, 13)).Select(d => Day.Dense(d).Start(100.00));

        var result = Run(definition, Day.Join([.. history, monday, .. after]), Filed());

        var trade = result.Trades.ShouldHaveSingleItem();
        trade.StopPrice.ShouldBe(92.00, 1e-9);
        trade.EntryPrice.ShouldBe(100.00);
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
    }

    [Fact]
    public void The_atr_stop_is_not_ready_before_fourteen_sessions_and_takes_no_candidate()
    {
        var definition = TestStrategies.SwingDaily("days_since_results == 0", 5).WithBuy(b => b with { Stop = new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2), AtrPeriod: Quantity.Of(14)) });

        var result = Run(definition, Bars(Weekdays(Mon, new DateOnly(2026, 3, 13))), Filed());

        result.Outcomes.ShouldBeEmpty();
        result.Scans.Sum(s => s.StopNotReady).ShouldBe(1);
    }
}
