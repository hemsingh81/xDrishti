using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// P15-06 criteria 1 to 3 through the real <see cref="Simulator"/>: a strategy that names <c>not_event_day</c> runs (no longer <c>filter_not_available</c>), blocks an entry by the rule "no position is open across a results event" using only the rows visible at the decision instant, takes
/// no entry for an instrument with no entry (<c>unavailable</c>, counted), and a strategy that does not name the filter gives exactly the results it gave before, whatever snapshot it is handed.
/// </summary>
public sealed class EventFilterSimulatorTests
{
    private static readonly Guid Instrument = Scan.Instrument.Id;

    private static readonly StrategyDefinition Intraday = TestStrategies.OrbBuyOnly() with { Filters = ["not_event_day"] };

    private static readonly StrategyDefinition Swing = TestStrategies.Swing1h() with { Filters = ["not_event_day"] };

    private static SimulationResult Run(StrategyDefinition definition, IReadOnlyList<Bar> bars, EventCalendarSnapshot? events, Timeframe timeframe = Timeframe.M15, TradingCalendar? calendar = null)
    {
        var simulator = Sim.Create(definition, timeframe, Cand.NoSlippage);
        var result = simulator.Run(Scan.Instrument, bars, Sim.Tick, calendar ?? DefaultWeek(), null, null, events);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    [Fact]
    public void A_strategy_that_names_the_filter_is_no_longer_refused_and_min_turnover_still_is_with_a_message_that_does_not_mention_the_calendar()
    {
        var named = Simulator.Create(Intraday, Timeframe.M15);
        var turnover = Simulator.Create(TestStrategies.OrbBuyOnly() with { Filters = ["min_turnover"] }, Timeframe.M15);

        named.Simulator.ShouldNotBeNull(string.Join("; ", named.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        named.Simulator.ReadsEvents.ShouldBeTrue();
        turnover.Simulator.ShouldBeNull();
        var refusal = turnover.Problems.Single(p => p.Problem.Code == "simulation.filter_not_available");
        (refusal.Path, refusal.Problem.Message.Contains("calendar", StringComparison.OrdinalIgnoreCase)).ShouldBe(("filters[0]", false));
    }

    [Fact]
    public void An_entry_on_the_event_session_is_blocked_and_counted_and_opens_no_position()
    {
        var result = Run(Intraday, Cases.F("F1").Bars, EventRows.Snapshot(Instrument, Mon));

        result.Trades.ShouldBeEmpty();
        result.Outcomes.Single().Walk.NoTrade.ShouldBe(NoTradeReason.EventBlocked);
        (result.Walk.Candidates, result.Walk.EventBlocked, result.Walk.EventUnavailable, result.Walk.Trades).ShouldBe((1L, 1L, 0L, 0L));
        result.Walk.Candidates.ShouldBe(result.Walk.Trades + result.Walk.NoFillBar + result.Walk.AfterSquareOff + result.Walk.EntryThroughStop + result.Walk.LimitNotReached + result.Walk.PositionOpen + result.Walk.OrderPendingAtEnd + result.Walk.EventBlocked + result.Walk.EventUnavailable);
    }

    [Fact]
    public void An_intraday_entry_the_day_before_the_event_is_taken_and_equals_the_trade_of_the_same_strategy_without_the_filter()
    {
        var bars = Cases.F("F1").Bars;

        var filtered = Run(Intraday, bars, EventRows.Snapshot(Instrument, Tue));
        var plain = Sim.Run(Sim.Create(TestStrategies.OrbBuyOnly(), fills: Cand.NoSlippage), bars);

        OutcomeFormatter.Render(Result.Success(filtered.Outcomes.Single().Walk)).ShouldBe(OutcomeFormatter.Render(Result.Success(plain.Outcomes.Single().Walk)));
        filtered.Trades.ShouldHaveSingleItem();
        filtered.Walk.EventBlocked.ShouldBe(0);
    }

    [Fact]
    public void An_instrument_with_no_visible_entry_gets_no_entry_and_the_count_says_why()
    {
        var other = Guid.NewGuid();

        var noRows = Run(Intraday, Cases.F("F1").Bars, EventCalendarSnapshot.Create([], 5));
        var otherInstrument = Run(Intraday, Cases.F("F1").Bars, EventRows.Snapshot(other, Mon));

        foreach (var result in new[] { noRows, otherInstrument })
        {
            result.Trades.ShouldBeEmpty();
            result.Outcomes.Single().Walk.NoTrade.ShouldBe(NoTradeReason.EventUnavailable);
            (result.Walk.EventUnavailable, result.Walk.EventBlocked, result.Walk.Trades).ShouldBe((1L, 0L, 0L));
        }
    }

    [Fact]
    public void A_meeting_intimated_after_the_decision_instant_does_not_exist_at_it()
    {
        // The F1 candidate is decided on Monday 2026-03-02 at 09:45 IST. A meeting on that day intimated at 09:50 is unknown then; one intimated 09:39 plus the 5-minute lag is known at 09:44.
        var late = EventRows.Snapshot(Instrument, Mon, intimated: new DateTimeOffset(2026, 3, 2, 9, 50, 0, EventRows.Ist));
        var early = EventRows.Snapshot(Instrument, Mon, intimated: new DateTimeOffset(2026, 3, 2, 9, 39, 0, EventRows.Ist));

        Run(Intraday, Cases.F("F1").Bars, late).Outcomes.Single().Walk.NoTrade.ShouldBe(NoTradeReason.EventUnavailable);
        Run(Intraday, Cases.F("F1").Bars, early).Outcomes.Single().Walk.NoTrade.ShouldBe(NoTradeReason.EventBlocked);
    }

    [Theory]
    [InlineData(2026, 3, 5, true)]    // Thursday: three sessions after Monday, inside the hold of 3
    [InlineData(2026, 3, 6, false)]   // Friday: four sessions away
    [InlineData(2026, 3, 2, true)]    // Monday: the event session itself
    public void A_swing_entry_is_blocked_when_the_event_is_within_the_hold_of_the_side(int year, int month, int day, bool blocked)
    {
        var result = Run(Swing, FixtureR.Bars(Fri), EventRows.Snapshot(Instrument, new DateOnly(year, month, day)), Timeframe.H1);

        var outcome = result.Outcomes.ShouldHaveSingleItem();
        (outcome.Walk.NoTrade == NoTradeReason.EventBlocked).ShouldBe(blocked);
        (outcome.Walk.Trade is not null).ShouldBe(!blocked);
    }

    [Fact]
    public void A_swing_hold_of_two_sessions_is_blocked_by_an_event_two_sessions_away_and_not_by_three()
    {
        var shortHold = TestStrategies.Swing1h(2) with { Filters = ["not_event_day"] };

        Run(shortHold, FixtureR.Bars(Fri), EventRows.Snapshot(Instrument, Wed), Timeframe.H1).Outcomes.Single().Walk.NoTrade.ShouldBe(NoTradeReason.EventBlocked);
        Run(shortHold, FixtureR.Bars(Fri), EventRows.Snapshot(Instrument, Thu), Timeframe.H1).Outcomes.Single().Walk.Trade.ShouldNotBeNull();
    }

    [Fact]
    public void A_blocked_candidate_does_not_occupy_the_position_gate_and_unavailable_is_never_a_pass()
    {
        // Two swing candidates on one instrument: the first is blocked by the event, so it opens no position and the gate cannot make the second a PositionOpen for it.
        var result = Run(Swing, FixtureR.Bars(Fri), EventRows.Snapshot(Instrument, Mon), Timeframe.H1);

        result.Walk.PositionOpen.ShouldBe(0);
        result.Outcomes.ShouldAllBe(o => o.Walk.Trade == null);
        result.Outcomes.ShouldAllBe(o => o.Walk.NoTrade == NoTradeReason.EventBlocked || o.Walk.NoTrade == NoTradeReason.EventUnavailable);
    }

    [Fact]
    public void A_strategy_that_does_not_name_the_filter_gives_the_same_results_with_or_without_a_snapshot_and_a_snapshot_is_never_read()
    {
        foreach (var name in new[] { "F1", "F2", "F5", "F13" })
        {
            var bars = Cases.F(name).Bars;
            var simulator = Sim.Create(OrbV1.Create(), fills: Cand.NoSlippage);

            var without = simulator.Run(Scan.Instrument, bars, Sim.Tick, DefaultWeek());
            var blocking = simulator.Run(Scan.Instrument, bars, Sim.Tick, DefaultWeek(), null, null, EventRows.Snapshot(Instrument, Mon));

            simulator.ReadsEvents.ShouldBeFalse();
            string.Join("\n", blocking.Value.Outcomes.Select(o => OutcomeFormatter.Render(Result.Success(o.Walk)))).ShouldBe(string.Join("\n", without.Value.Outcomes.Select(o => OutcomeFormatter.Render(Result.Success(o.Walk)))), name);
            blocking.Value.Walk.ShouldBe(without.Value.Walk, name);
            blocking.Value.Walk.EventBlocked.ShouldBe(0);
            blocking.Value.Walk.EventUnavailable.ShouldBe(0);
        }
    }

    [Fact]
    public void Naming_the_filter_with_no_snapshot_or_no_calendar_is_a_refusal_not_an_exception_and_not_a_silent_pass()
    {
        var simulator = Sim.Create(Intraday, fills: Cand.NoSlippage);

        var none = simulator.Run(Scan.Instrument, Cases.F("F1").Bars, Sim.Tick, DefaultWeek());
        var noCalendar = simulator.Run(Scan.Instrument, Cases.F("F1").Bars, Sim.Tick, null, null, null, EventRows.Snapshot(Instrument, Tue));

        none.IsFailure.ShouldBeTrue();
        none.Error.Code.ShouldBe("simulation.filter_not_available");
        noCalendar.IsFailure.ShouldBeTrue();
        noCalendar.Error.Code.ShouldBe("simulation.filter_not_available");
    }

    [Fact]
    public void A_comparison_and_a_direct_walk_of_a_side_that_names_the_filter_are_refused_without_the_snapshot_never_run_as_if_it_passed()
    {
        var members = new[] { Intraday, Intraday };
        var comparison = SimulatorComparison.Create(members, Timeframe.M15, Cand.NoSlippage);
        comparison.Comparison.ShouldNotBeNull();

        var none = comparison.Comparison.Run(Scan.Instrument, Cases.F("F1").Bars, Sim.Tick, DefaultWeek());
        var noCalendar = comparison.Comparison.Run(Scan.Instrument, Cases.F("F1").Bars, Sim.Tick, null, null, null, EventRows.Snapshot(Instrument, Tue));
        var withBoth = comparison.Comparison.Run(Scan.Instrument, Cases.F("F1").Bars, Sim.Tick, DefaultWeek(), null, null, EventRows.Snapshot(Instrument, Mon));

        none.Error.Code.ShouldBe("simulation.filter_not_available");
        noCalendar.Error.Code.ShouldBe("simulation.filter_not_available");
        withBoth.Value.Members.ShouldAllBe(m => m.Outcomes.Single().Walk.NoTrade == NoTradeReason.EventBlocked);

        // WalkSide itself (the seam the comparison and the run share) refuses too.
        var simulator = Sim.Create(Intraday, fills: Cand.NoSlippage);
        var scanner = XDrishti.Simulation.Scanning.EntryScanner.Create(simulator.CompiledOf(Side.Buy)!, Scan.Instrument, DefaultWeek(), null).Value;
        foreach (var bar in Cases.F("F1").Bars)
        {
            scanner.Step(bar);
        }

        simulator.WalkSide(simulator.SideRuns[0], scanner, Cases.F("F1").Bars, Sim.Tick, DefaultWeek(), null, false, null).Error.Code.ShouldBe("simulation.filter_not_available");
    }

    [Fact]
    public void A_covered_instrument_whose_last_meeting_has_passed_and_whose_next_is_not_yet_intimated_stays_unavailable_never_a_guess()
    {
        // Fail closed (ADR 0182): the meeting was on Friday 2026-02-27; the next one is not in the calendar at the Monday decision. Entries are possible only between an intimation and its meeting.
        var passed = EventRows.Snapshot(Instrument, new DateOnly(2026, 2, 27));

        var result = Run(Intraday, Cases.F("F1").Bars, passed);

        passed.Answer(Instrument, new DateTimeOffset(2026, 3, 2, 10, 0, 0, EventRows.Ist), DefaultWeek()).Reason.ShouldBe(ResultsAnswer.NoUpcomingEntry);
        result.Trades.ShouldBeEmpty();
        result.Outcomes.Single().Walk.NoTrade.ShouldBe(NoTradeReason.EventUnavailable);
    }

    [Fact]
    public void The_walk_reads_the_snapshot_by_a_dictionary_lookup_only_so_the_hash_of_the_same_rows_is_the_same_in_any_order()
    {
        var a = EventRows.Meeting(Instrument, Tue, 1);
        var b = EventRows.Meeting(Guid.NewGuid(), Wed, 2);

        EventCalendarSnapshot.Create([a, b], 5).Sha256.ShouldBe(EventCalendarSnapshot.Create([b, a], 5).Sha256);
    }
}
