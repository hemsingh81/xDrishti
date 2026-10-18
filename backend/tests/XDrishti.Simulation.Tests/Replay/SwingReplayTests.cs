using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using XDrishti.Strategies.BuiltIn;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>
/// Criteria 13, 18 (the table), 19 and 20 of P3-03d through the real <see cref="Simulator"/> and <see cref="InstrumentReplayer"/>: fixture R (the 1h swing fixture), the window rows J1 to J4, the flagged boundary Q1 and Q2 and the fates.
/// </summary>
public sealed class SwingReplayTests
{
    private static readonly PreparedStrategy Swing = Rep.Prepare(TestStrategies.Swing1h(), Timeframe.H1, FillRules.Default);

    private static decimal Q(double value) => Math.Round((decimal)value, 4);

    private static InstrumentReplay Replay(IReadOnlyList<Bar> bars, DateOnly from, DateOnly to, ReplayOptions? options = null) =>
        Rep.Replay(Swing, bars, from, to, calendar: DefaultWeek(), options: options);

    [Fact]
    public void The_simulator_makes_one_candidate_on_monday_at_1115_and_its_trade_is_the_hand_made_candidate_walked_by_the_walker_fixture_R()
    {
        var bars = FixtureR.Bars(Fri);

        var result = Sim.Run(TestStrategies.Swing1h(), bars, Timeframe.H1, calendar: DefaultWeek());

        var outcome = result.Outcomes.ShouldHaveSingleItem();
        var candidate = outcome.Candidate;
        candidate.DecisionEpochSeconds.ShouldBe(Day.Epoch(Mon, Day.Minute("11:15")));
        candidate.TriggerBar.Close.ShouldBe(101.50);
        candidate.StopLevel.ShouldBe(98.00);
        var index = Array.FindIndex(bars, b => b.EpochSeconds == Day.Epoch(Mon, Day.Minute("11:14")));
        var byHand = new EntryCandidate("SWING_1H_TEST", candidate.Instrument, Side.Buy, index, Day.Epoch(Mon, Day.Minute("11:15")), Timeframe.H1, candidate.TriggerBar, 60, true, 98.00, DecisionTrace.Empty);
        var rules = new WalkRules(2.0, WalkRules.NoSquareOff, FillRules.Default, Sim.Tick, SwingHold.Create(3, DefaultWeek()).Value);
        var walked = TradeWalker.Walk(byHand, bars, rules);

        OutcomeFormatter.Render(Domain.Common.Result.Success(outcome.Walk)).ShouldBe(OutcomeFormatter.Render(walked));
        var trade = outcome.Walk.Trade.ShouldNotBeNull();
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
        trade.EntryPrice.ShouldBe(101.50);
        Q(trade.EntryFilledPrice).ShouldBe(101.55m);
        trade.TargetPrice.ShouldBe(108.50);
        trade.ExitEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("15:29")));
        Q(trade.ExitPrice).ShouldBe(101.70m);
        Q(trade.ExitFilledPrice).ShouldBe(101.60m);
        Q(trade.RExact).ShouldBe(0.0571m);
        Q(trade.RFilled).ShouldBe(0.0143m);
        Q(trade.SlippageR).ShouldBe(-0.0429m);
        Q(trade.MaeR).ShouldBe(0.0000m);
        Q(trade.MfeR).ShouldBe(0.0857m);
        (trade.SessionsHeld, trade.SessionsUnseen).ShouldBe((3, 0));
        result.Header.EngineVersion.ShouldBe("sim-4");
        result.Header.SchemaVersion.ShouldBe(4);
        result.Walk.PositionOpen.ShouldBe(0);
    }

    [Fact]
    public void A_tuesday_that_opens_through_the_stop_exits_at_the_open_and_is_an_overnight_gap_fixture_R()
    {
        var bars = FixtureR.Bars(Fri, "09:15 97.40/97.60/97.30/97.50");

        var result = Sim.Run(TestStrategies.Swing1h(), bars, Timeframe.H1, calendar: DefaultWeek());

        var outcome = result.Outcomes.Single().Walk;
        var trade = outcome.Trade.ShouldNotBeNull();
        trade.ExitReason.ShouldBe(ExitReason.Stop);
        Q(trade.ExitPrice).ShouldBe(97.40m);
        Q(trade.ExitFilledPrice).ShouldBe(97.20m);
        Q(trade.RExact).ShouldBe(-1.1714m);
        Q(trade.RFilled).ShouldBe(-1.2429m);
        Q(trade.SlippageR).ShouldBe(-0.0714m);
        Q(trade.MaeR).ShouldBe(1.1714m);
        (outcome.StopGapExit, outcome.OvernightStopGap).ShouldBe((true, true));
        result.Walk.StopGapExits.ShouldBe(1);
        result.Walk.OvernightStopGaps.ShouldBe(1);
        trade.SessionsHeld.ShouldBe(2);
    }

    [Fact]
    public void J1_a_window_through_wednesday_gives_the_trade_of_the_fixture()
    {
        var replay = Replay(FixtureR.Bars(Wed), Mon, Wed);

        var trade = replay.Trades.ShouldHaveSingleItem();
        trade.ExitEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("15:29")));
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
        Q(trade.RExact).ShouldBe(0.0571m);
        replay.OpenAtEnd.ShouldBeEmpty();
        replay.Record.Fates.Trades.ShouldBe(1);
    }

    [Fact]
    public void J2_a_window_that_ends_on_tuesday_reports_the_position_as_open_at_the_end_with_the_same_entry()
    {
        var j1 = Replay(FixtureR.Bars(Wed), Mon, Wed).Trades.Single();

        var replay = Replay(FixtureR.Bars(Tue), Mon, Tue);

        replay.Trades.ShouldBeEmpty();
        var open = replay.OpenAtEnd.ShouldHaveSingleItem();
        open.ExitReason.ShouldBe(ExitReason.EndOfData);
        open.ExitEpochSeconds.ShouldBe(Day.Epoch(Tue, Day.Minute("15:29")));
        open.ExitPrice.ShouldBe(101.50);
        open.RExact.ShouldBe(0.0);
        (open.EntryEpochSeconds, open.EntryPrice, open.StopPrice, open.TargetPrice, open.EntryFilledPrice, open.EntryFill, open.TickFallback)
            .ShouldBe((j1.EntryEpochSeconds, j1.EntryPrice, j1.StopPrice, j1.TargetPrice, j1.EntryFilledPrice, j1.EntryFill, j1.TickFallback));
        replay.Record.Fates.OpenAtEnd.ShouldBe(1);
        replay.Record.Fates.Trades.ShouldBe(0);
    }

    [Fact]
    public void J3_a_window_that_starts_on_tuesday_counts_the_trade_before_the_window_and_the_candidate_is_not_in_the_window()
    {
        var replay = Replay(FixtureR.Bars(Fri), Tue, Fri);

        replay.Trades.ShouldBeEmpty();
        replay.OpenAtEnd.ShouldBeEmpty();
        replay.Record.Fates.TradesBeforeWindow.ShouldBe(1);
        replay.Record.CandidatesInWindow.ShouldBe(0);
    }

    [Fact]
    public void J4_a_window_through_friday_gives_the_trade_of_J1_byte_for_byte()
    {
        var j1 = Replay(FixtureR.Bars(Wed), Mon, Wed).Trades.Single();

        var replay = Replay(FixtureR.Bars(Fri), Mon, Fri);

        RunText.Trade(replay.Trades.ShouldHaveSingleItem()).ShouldBe(RunText.Trade(j1));
        replay.Record.Fates.NoTrade.ShouldBe(0);
    }

    [Fact]
    public void Q1_a_position_open_at_the_end_of_a_segment_that_a_flagged_day_follows_is_excluded_and_counted()
    {
        // Tuesday opens at 60.00 against a close of 101.50: abs(60 - 101.50) = 41.50 > 20.30, flagged.
        var replay = Replay(FixtureR.CrashBars(Wed, "09:15 60.00/60.00/60.00/60.00"), Mon, Wed);

        replay.Trades.ShouldBeEmpty();
        replay.OpenAtEnd.ShouldBeEmpty();
        var fates = replay.Record.Fates;
        (fates.ExcludedAcrossDiscontinuity, fates.OpenAtEnd, fates.Trades).ShouldBe((1L, 0L, 0L));
        replay.Record.Discontinuities.ShouldBe([new DiscontinuityNote(Tue, 60.00 / 101.50, 1)]);
        replay.Record.Discontinuities[0].ExcludedPositions.ShouldBe(1);
        fates.Total.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates));
        replay.Record.TradesInWindow.ShouldBe(0);
    }

    [Fact]
    public void Q1_the_exclusion_is_in_the_totals_of_the_run_too()
    {
        var replay = Replay(FixtureR.CrashBars(Wed, "09:15 60.00/60.00/60.00/60.00"), Mon, Wed);
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(Rep.Id, "TEST")]);
        var header = new RunHeader(Swing.Record, SimulationEngine.Version, Mon, Wed, ReplayOptions.Default, false, Swing.Fills.ToCanonicalJson(), universe, DefaultWeek());

        var run = RunAssembler.Assemble(header, [replay]);

        run.Totals.Fates.ExcludedAcrossDiscontinuity.ShouldBe(1);
        run.Totals.DiscontinuitiesFlagged.ShouldBe(1);
        run.Totals.Fates.Total.ShouldBe(run.Totals.Scans.Sum(s => s.Candidates));
        run.SchemaVersion.ShouldBe(4);
    }

    [Fact]
    public void Q1_the_run_goes_on_to_friday_the_exclusion_alone_applies_and_the_fates_still_add_up_to_the_candidates()
    {
        // The flagged Tuesday flat at 60 makes no candidate for the 1h trigger, so the fate that applies is the exclusion alone and the identity holds.
        var replay = Replay(FixtureR.CrashBars(Fri, "09:15 60.00/60.00/60.00/60.00"), Mon, Fri);

        replay.Record.Fates.EntriesSuppressed.ShouldBe(0);
        replay.Record.Fates.ExcludedAcrossDiscontinuity.ShouldBe(1);
        replay.Record.Fates.Total.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates));
    }

    [Fact]
    public void Q2_a_gap_under_the_threshold_is_not_flagged_and_the_position_stops_at_the_open_with_a_large_r()
    {
        // abs(81.30 - 101.50) = 20.20 <= 20.30: not flagged; the stop 98.00 is gapped through at 81.30.
        var replay = Replay(FixtureR.CrashBars(Wed, "09:15 81.30/81.50/81.00/81.20"), Mon, Wed);

        replay.Record.Discontinuities.ShouldBeEmpty();
        var trade = replay.Trades.ShouldHaveSingleItem();
        trade.ExitReason.ShouldBe(ExitReason.Stop);
        Q(trade.ExitPrice).ShouldBe(81.30m);
        Q(trade.ExitFilledPrice).ShouldBe(81.10m);
        Q(trade.RExact).ShouldBe(-5.7714m);
        Q(trade.RFilled).ShouldBe(-5.8429m);
        Q(trade.SlippageR).ShouldBe(-0.0714m);
        Q(trade.MaeR).ShouldBe(5.7714m);
        replay.Record.Walk.StopGapExits.ShouldBe(1);
        replay.Record.Fates.ExcludedAcrossDiscontinuity.ShouldBe(0);
    }

    [Fact]
    public void An_intraday_end_of_data_in_a_segment_before_a_flagged_day_is_still_open_at_the_end()
    {
        // F6 ends at 15:09 (an EndOfData ORB trade), the next day opens at twice the close: flagged. The rule is by strategy style, so the intraday trade is unchanged.
        var orb = Rep.Prepare(OrbV1.Create());
        var bars = ReplayDays.Join(ReplayDays.F6(ReplayDays.D1), ReplayDays.Scaled(ReplayDays.F1(ReplayDays.D2), 2.0));

        var replay = Rep.Replay(orb, bars, ReplayDays.D1, ReplayDays.D2);

        replay.Record.Discontinuities.Count.ShouldBe(1);
        replay.Record.Discontinuities[0].ExcludedPositions.ShouldBe(0);
        replay.Record.Fates.OpenAtEnd.ShouldBe(1);
        replay.Record.Fates.ExcludedAcrossDiscontinuity.ShouldBe(0);
        replay.OpenAtEnd.Single().ExitReason.ShouldBe(ExitReason.EndOfData);
    }

    [Fact]
    public void The_replay_has_a_case_for_every_no_trade_reason_and_each_is_one_candidate_found_by_reflection_over_the_enum()
    {
        // A reflection over the enum: a reason added later cannot be dropped silently (the routine of the replayer throws on a reason it does not know).
        var reasons = Enum.GetValues<NoTradeReason>();
        reasons.ShouldBe([NoTradeReason.NoFillBar, NoTradeReason.AfterSquareOff, NoTradeReason.EntryThroughStop, NoTradeReason.LimitNotReached, NoTradeReason.PositionOpen]);

        var seen = new HashSet<string>();
        foreach (var reason in reasons)
        {
            var fates = InstrumentReplayer.NoTradeFate(reason, "TEST");

            fates.NoTrade.ShouldBe(1, reason.ToString());
            fates.Total.ShouldBe(1, reason.ToString());
            seen.Add(fates.ToString()).ShouldBeTrue(reason + " has its own field");
        }

        Should.Throw<InvalidOperationException>(() => InstrumentReplayer.NoTradeFate((NoTradeReason)99, "TEST")).Message.ShouldContain("TEST");
        Should.Throw<InvalidOperationException>(() => InstrumentReplayer.NoTradeFate(null, "TEST"));
    }

    [Fact]
    public void A_second_signal_decided_inside_the_position_is_a_position_open_fate_through_the_replay()
    {
        // Tuesday breaks out of its own opening range at 10:30 (the 1h bar of 10:15 to 11:15 closes at 103.00 above the 101.50 of its first hour), while Monday's position (held up to Wednesday) is open.
        var replay = Replay(FixtureR.Bars(Fri, "10:30 101.50/103.00/101.50/103.00"), Mon, Fri);

        var fates = replay.Record.Fates;
        (fates.Trades, fates.PositionOpen).ShouldBe((1L, 1L));
        fates.Total.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates));
        replay.Record.Scans.Sum(s => s.Candidates).ShouldBe(2);
        replay.Record.Walk.PositionOpen.ShouldBe(1);
        replay.Trades.Single().ExitEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("15:29")));
    }

    [Fact]
    public void Named_limit_3_a_candidate_dropped_as_suppressed_on_a_flagged_day_still_occupies_the_gate_of_its_segment()
    {
        // Monday is flat at 100. Tuesday opens at 60 (flagged: abs(60 - 100) = 40 > 20) and breaks out of its own first hour at 11:15 (62 against 60): the new segment's scanner makes a candidate, the swing walk enters it at 62,
        // and the replay drops its outcome as suppressed. Wednesday breaks out at 11:15 (64 against 62): a valid candidate, but the position of the suppressed one is open (the list ends on Wednesday), so the gate blocks it.
        var monday = Day.Dense(Mon).Start(100.00);
        var tuesday = Day.Dense(Tue).Start(60.00).Set("10:30 60.00/62.00/60.00/62.00");
        var wednesday = Day.After(tuesday, Wed).Set("10:30 62.00/64.00/62.00/64.00");

        var replay = Replay(Day.Join(monday, tuesday, wednesday), Mon, Wed);

        var fates = replay.Record.Fates;
        (fates.EntriesSuppressed, fates.PositionOpen, fates.Trades, fates.OpenAtEnd, fates.ExcludedAcrossDiscontinuity).ShouldBe((1L, 1L, 0L, 0L, 0L));
        fates.Total.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates));
        replay.Record.Scans.Sum(s => s.Candidates).ShouldBe(2);
        replay.Record.Discontinuities.Select(d => d.Date).ShouldBe([Tue]);
        replay.Trades.ShouldBeEmpty();
        replay.OpenAtEnd.ShouldBeEmpty();

        // The same Wednesday without Tuesday's breakout (Tuesday flat at 60, so Wednesday breaks out of 60 to 64): nothing is suppressed, nothing is blocked, and the position is open at the end.
        var flatTuesday = Day.Dense(Tue).Start(60.00);
        var control = Replay(Day.Join(monday, flatTuesday, Day.After(flatTuesday, Wed).Set("10:30 60.00/64.00/60.00/64.00")), Mon, Wed).Record.Fates;
        (control.EntriesSuppressed, control.PositionOpen, control.Trades, control.OpenAtEnd).ShouldBe((0L, 0L, 0L, 1L));
    }

    [Fact]
    public void The_run_key_starts_with_the_schema_4_line_and_a_holiday_inside_a_hold_changes_it()
    {
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(Rep.Id, "TEST")]);
        string Key(Domain.Market.Calendar.TradingCalendar calendar)
        {
            var replay = Rep.Replay(Swing, FixtureR.Bars(Fri), Mon, Fri, calendar: calendar);
            var header = new RunHeader(Swing.Record, SimulationEngine.Version, Mon, Fri, ReplayOptions.Default, false, Swing.Fills.ToCanonicalJson(), universe, calendar);
            return RunAssembler.Assemble(header, [replay]).RunKey;
        }

        var plain = Key(DefaultWeek());
        var holiday = Key(HolidayOnTuesday());
        var instruments = new[] { InstrumentRun.NotFound(Rep.Id) };
        var text = RunAssembler.KeyText(new RunHeader(Swing.Record, SimulationEngine.Version, Mon, Fri, ReplayOptions.Default, false, Swing.Fills.ToCanonicalJson(), universe, DefaultWeek()), instruments, []);

        text.ShouldStartWith("xd-run 4\n");
        SimulationRun.CurrentSchemaVersion.ShouldBe(4);
        holiday.ShouldNotBe(plain, "a run that counted a holiday differently has a different key");
    }
}
