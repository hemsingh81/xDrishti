using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>
/// Criteria 11, 16 to 20 and 24 of P3-03c through the real <see cref="InstrumentReplayer"/>: W7 and W8 (the F1 and F2 days of P3-03) against the hand figures, the costed golden file paired with them, the fates of the skips and their identity,
/// the run record and its key with a <see cref="CostRecord"/>, the fail-closed date, the position gate that counts a trade costing skipped, and determinism.
/// </summary>
public sealed class CostedReplayTests
{
    private static readonly PreparedStrategy Bare = Rep.Prepare(OrbV1.Create());

    private static readonly decimal[] Limits = [0.0001m, 0.0377m, 0.0382m, 0.10m, 100m];

    private static readonly decimal[] Capitals = [100m, 1_000m, 10_000m, 100_000m, 1_000_000m];

    private static readonly PreparedStrategy Swing = Rep.Prepare(TestStrategies.Swing1h(), Timeframe.H1, FillRules.Default);

    private static CostContext Context(decimal capital = 1_000_000m, decimal maxCostR = 0.10m) => TestCostSheet.Context(capital, maxCostR);

    private static InstrumentReplay Costed(IReadOnlyList<Bar> bars, DateOnly from, DateOnly to, CostContext? costs = null) => Rep.Replay(Bare, bars, from, to, costs: costs ?? Context());

    private static InstrumentReplay Gross(IReadOnlyList<Bar> bars, DateOnly from, DateOnly to) => Rep.Replay(Bare, bars, from, to);

    private static Bar[] TwoDays() => ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.F1(ReplayDays.D2));

    [Fact]
    public void W7_the_F1_day_replayed_on_the_first_schedule_equals_the_hand_figures_line_by_line()
    {
        var replay = Costed(ReplayDays.F1(ReplayDays.D1), ReplayDays.D1, ReplayDays.D1);

        var trade = replay.Trades.ShouldHaveSingleItem();
        trade.TradeDate.ShouldBe(ReplayDays.D1, "the test's date decides the schedule: 2026-03-02 is schedule A");
        (trade.EntryPrice, trade.StopPrice, trade.ExitPrice, trade.ExitReason, trade.RExact, trade.RFilled).ShouldBe((100.50, 98.00, 105.50, ExitReason.Target, 2.0, 2.0));
        WorkedAssert.ShouldMatch(trade, WorkedCosts.W7A);
        trade.Costing!.RNet.ShouldBe(1.96029, 1e-9);
    }

    [Fact]
    public void W7_the_F1_day_replayed_on_the_second_schedule_equals_the_hand_figures_and_costs_ten_rupees_fifty_five_paise_less()
    {
        var replay = Costed(ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.F1(ReplayDays.D2)), ReplayDays.D1, ReplayDays.D2);

        var trade = replay.Trades.ShouldHaveSingleItem();
        trade.TradeDate.ShouldBe(ReplayDays.D2, "2026-03-03 is schedule B, as in the run-record golden file");
        WorkedAssert.ShouldMatch(trade, WorkedCosts.W7B);
        (WorkedCosts.W7A.Total - WorkedCosts.W7B.Total).ShouldBe(10.55m, "2,11,000 x 0.005 %");
        trade.Costing!.RNet.ShouldBe(1.9624, 1e-9);
    }

    [Fact]
    public void W8_the_F2_day_stopped_out_has_a_net_R_below_its_filled_R_by_the_charge()
    {
        var replay = Costed(ReplayDays.F2(ReplayDays.D1), ReplayDays.D1, ReplayDays.D1);

        var trade = replay.Trades.ShouldHaveSingleItem();
        (trade.ExitReason, trade.RExact, trade.RFilled).ShouldBe((ExitReason.Stop, -1.0, -1.0));
        WorkedAssert.ShouldMatch(trade, WorkedCosts.W8);
        trade.Costing!.RNet.ShouldBe(-1.038314, 1e-9);
        trade.Costing.RNet.ShouldBeLessThan(trade.RFilled);
    }

    [Fact]
    public void The_costed_replay_differs_from_the_gross_replay_only_in_costs_and_costing_and_in_the_skipped_trades()
    {
        var bars = ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.F1(ReplayDays.D2), ReplayDays.F6(ReplayDays.D3));

        var gross = Gross(bars, ReplayDays.D1, ReplayDays.D3);
        var costed = Costed(bars, ReplayDays.D1, ReplayDays.D3);

        costed.Trades.Count.ShouldBe(gross.Trades.Count);
        costed.OpenAtEnd.Count.ShouldBe(gross.OpenAtEnd.Count);
        costed.Trades.Select(t => t with { Costing = null, Costs = CostBasis.None }).ShouldBe(gross.Trades);
        costed.OpenAtEnd.Select(t => t with { Costing = null, Costs = CostBasis.None }).ShouldBe(gross.OpenAtEnd);
        costed.Trades.Concat(costed.OpenAtEnd).ShouldAllBe(t => t.Costs == CostBasis.Charged && t.Costing != null);
        gross.Trades.Concat(gross.OpenAtEnd).ShouldAllBe(t => t.Costs == CostBasis.None && t.Costing == null);
        costed.Record.Fates.ShouldBe(gross.Record.Fates, "no trade was skipped");
        costed.Record.Walk.ShouldBe(gross.Record.Walk);
        costed.Record.Scans.Select(Render.Counters).ShouldBe(gross.Record.Scans.Select(Render.Counters));
        costed.OpenAtEnd.Single().Costing!.Quantity.ShouldBeGreaterThan(0, "an EndOfData trade is costed like any other and stays in OpenAtEnd");
    }

    [Fact]
    public void A_trade_skipped_by_cost_leaves_the_lists_and_is_counted_once_and_the_totals_walk_still_counts_it()
    {
        var bars = TwoDays();
        var gross = Gross(bars, ReplayDays.D1, ReplayDays.D2);

        var replay = Costed(bars, ReplayDays.D1, ReplayDays.D2, Context(maxCostR: 0.0381m));

        // F2 on the first schedule costs 0.038314 R and F1 on the second 0.0376 R: the limit 0.0381 keeps the second and skips the first
        var fates = replay.Record.Fates;
        (fates.Trades, fates.SkippedCost, fates.SkippedQuantity, fates.OpenAtEnd).ShouldBe((1L, 1L, 0L, 0L));
        replay.Trades.ShouldHaveSingleItem().TradeDate.ShouldBe(ReplayDays.D2);
        replay.Record.TradesInWindow.ShouldBe(1, "the trades kept");
        fates.Total.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates));
        (fates.Trades + fates.OpenAtEnd + fates.SkippedQuantity + fates.SkippedCost).ShouldBe(gross.Record.Fates.Trades + gross.Record.Fates.OpenAtEnd, "the trades the walker gave in the window");
        replay.Record.Walk.ExitsStop.ShouldBe(gross.Record.Walk.ExitsStop, "the walker's counters are the walker's: they still count the skipped trade");
        replay.Record.Walk.ShouldBe(gross.Record.Walk);
    }

    [Fact]
    public void A_trade_skipped_by_quantity_is_counted_as_such_and_quantity_comes_before_cost()
    {
        // capital 100: the risk amount is 0.50, byRisk 0.2 shares: the quantity is 0 for both days, whatever the charge would have been (the limit is far below any charge)
        var replay = Costed(TwoDays(), ReplayDays.D1, ReplayDays.D2, Context(capital: 100m, maxCostR: 0.0001m));

        var fates = replay.Record.Fates;
        (fates.SkippedQuantity, fates.SkippedCost, fates.Trades).ShouldBe((2L, 0L, 0L));
        replay.Trades.ShouldBeEmpty();
        replay.Record.TradesInWindow.ShouldBe(0);
    }

    [Fact]
    public void The_fates_of_the_skips_hold_the_identity_on_every_fixture_and_are_zero_in_a_gross_replay()
    {
        var bars = ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.F1(ReplayDays.D2), ReplayDays.F6(ReplayDays.D3));
        var gross = Gross(bars, ReplayDays.D1, ReplayDays.D3);

        gross.Record.Fates.SkippedQuantity.ShouldBe(0);
        gross.Record.Fates.SkippedCost.ShouldBe(0);
        foreach (var maxCostR in Limits)
        {
            foreach (var capital in new[] { 100m, 10_000m, 1_000_000m })
            {
                var replay = Costed(bars, ReplayDays.D1, ReplayDays.D3, Context(capital, maxCostR));
                var fates = replay.Record.Fates;

                (fates.Trades + fates.OpenAtEnd + fates.SkippedQuantity + fates.SkippedCost).ShouldBe(gross.Record.Fates.Trades + gross.Record.Fates.OpenAtEnd, $"capital {capital}, limit {maxCostR}");
                replay.Record.TradesInWindow.ShouldBe(replay.Trades.Count + replay.OpenAtEnd.Count);
                replay.Record.Fates.Total.ShouldBe(gross.Record.Fates.Total);
            }
        }
    }

    [Fact]
    public void Raising_the_limit_never_raises_the_cost_skips_and_raising_the_capital_never_raises_the_quantity_skips()
    {
        var bars = ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.F1(ReplayDays.D2), ReplayDays.F6(ReplayDays.D3));

        var costSkips = Limits.Select(m => Costed(bars, ReplayDays.D1, ReplayDays.D3, Context(maxCostR: m)).Record.Fates.SkippedCost).ToArray();
        var quantitySkips = Capitals.Select(c => Costed(bars, ReplayDays.D1, ReplayDays.D3, Context(c, 100m)).Record.Fates.SkippedQuantity).ToArray();

        costSkips.Zip(costSkips.Skip(1), (a, b) => a >= b).ShouldAllBe(x => x);
        quantitySkips.Zip(quantitySkips.Skip(1), (a, b) => a >= b).ShouldAllBe(x => x);
        costSkips[0].ShouldBeGreaterThan(0);
        quantitySkips[0].ShouldBeGreaterThan(0);
        costSkips[^1].ShouldBe(0);
        quantitySkips[^1].ShouldBe(0);
    }

    [Fact]
    public void A_trade_dated_before_the_first_schedule_throws_in_the_replay_and_is_never_a_zero_cost_trade()
    {
        var late = TestCostSheet.Of("test-late", TestCostSheet.Schedule(new DateOnly(2026, 3, 4), TestCostSheet.IntradayA, TestCostSheet.DeliveryA));
        var costs = TestCostSheet.Context(sheet: late);

        var thrown = Should.Throw<InvalidOperationException>(() => Rep.Replay(Bare, ReplayDays.F1(ReplayDays.D1), ReplayDays.D1, ReplayDays.D1, costs: costs));

        thrown.Message.ShouldContain("costs.no_rates_for_date");
        thrown.Message.ShouldContain("TEST");
    }

    [Fact]
    public void A_trade_before_the_window_is_not_costed_so_a_sheet_that_starts_at_from_prices_the_window()
    {
        var sheetFromD2 = TestCostSheet.Of("test-from-d2", TestCostSheet.Schedule(ReplayDays.D2, TestCostSheet.IntradayB, TestCostSheet.DeliveryB));
        var bars = TwoDays();

        var replay = Rep.Replay(Bare, bars, ReplayDays.D2, ReplayDays.D2, costs: TestCostSheet.Context(sheet: sheetFromD2));

        replay.Record.Fates.TradesBeforeWindow.ShouldBe(1, "the F2 trade of D1 is before the window and is dropped uncosted");
        replay.Trades.ShouldHaveSingleItem().Costing!.SheetId.ShouldBe("test-from-d2");
    }

    [Fact]
    public void An_instrument_skipped_before_the_scan_is_not_costed_and_a_trade_on_the_fallback_tick_is_costed_like_any_other()
    {
        var skipped = Rep.Replay(Bare, ReplayDays.F1(ReplayDays.D1), ReplayDays.D1, ReplayDays.D1, preset: SkipReason.CsvAdjustedTwice, costs: Context());
        var fallback = Rep.Replay(Bare, ReplayDays.F1(ReplayDays.D1), ReplayDays.D1, ReplayDays.D1, tick: TickSpec.FromMasterPaise(null).Value, costs: Context());

        skipped.Record.Skipped.ShouldBe(SkipReason.CsvAdjustedTwice);
        skipped.Trades.ShouldBeEmpty();
        (skipped.Record.Fates.SkippedQuantity, skipped.Record.Fates.SkippedCost).ShouldBe((0L, 0L));
        var trade = fallback.Trades.ShouldHaveSingleItem();
        trade.TickFallback.ShouldBeTrue();
        trade.Costing.ShouldNotBeNull().Quantity.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_costed_replay_is_the_same_twice_and_on_copies_in_every_field_and_in_text()
    {
        var bars = TwoDays();

        var first = Costed(bars, ReplayDays.D1, ReplayDays.D2);
        var second = Costed([.. bars], ReplayDays.D1, ReplayDays.D2, TestCostSheet.Context());

        RunText.Replay(first).ShouldBe(RunText.Replay(second));
        first.Trades.ShouldBe(second.Trades);
        RunText.HasNegativeZero(RunText.Replay(first)).ShouldBeFalse();
    }

    [Fact]
    public void The_costed_golden_file_agrees_with_the_hand_figures_line_by_line_and_is_checked_in()
    {
        var replay = Costed(TwoDays(), ReplayDays.D1, ReplayDays.D2);
        var f2 = replay.Trades.Single(t => t.TradeDate == ReplayDays.D1);
        var f1 = replay.Trades.Single(t => t.TradeDate == ReplayDays.D2);

        // the hand figures first: the golden file written by the code is not evidence by itself
        WorkedAssert.ShouldMatch(f2, WorkedCosts.W8);
        WorkedAssert.ShouldMatch(f1, WorkedCosts.W7B);
        var text = RunText.Costing(f2.Costing!) + "\n" + RunText.Costing(f1.Costing!) + "\n" + RunText.Replay(replay);

        GoldenFile.Check("costed", text);
        text.ShouldContain("total=191.57");
        text.ShouldContain("total=188 ");
    }

    [Fact]
    public void The_run_record_carries_the_cost_record_and_the_trades_of_a_costed_run_differ_from_the_gross_ones_only_in_costs_and_costing()
    {
        var bars = TwoDays();
        var strategy = Bare;
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(Rep.Id, "TEST")]);
        var context = Context();
        var costs = CostRecord.Of(context);
        RunHeader Header(CostRecord? record) => new(strategy.Record, SimulationEngine.Version, ReplayDays.D1, ReplayDays.D2, ReplayOptions.Default, false, strategy.Fills.ToCanonicalJson(), universe, Rep.DefaultCalendar, record);

        var grossRun = RunAssembler.Assemble(Header(null), [Gross(bars, ReplayDays.D1, ReplayDays.D2)]);
        var costedRun = RunAssembler.Assemble(Header(costs), [Costed(bars, ReplayDays.D1, ReplayDays.D2)]);

        grossRun.Costs.ShouldBeNull();
        costedRun.Costs.ShouldBe(costs);
        (costs.SheetId, costs.RatesSha256, costs.Checked, costs.Capital, costs.Profile, costs.MaxCostR).ShouldBe(("test-synthetic-1", TestCostSheet.Sheet.RatesSha256, null, 1_000_000m, SizingProfile.Standard, 0.10m));
        costs.SheetJson.ShouldBe(TestCostSheet.Json);
        costedRun.Trades.Select(t => t.Trade with { Costing = null, Costs = CostBasis.None }).ShouldBe(grossRun.Trades.Select(t => t.Trade));
        costedRun.RunKey.ShouldNotBe(grossRun.RunKey);
        costedRun.SchemaVersion.ShouldBe(5);
        costedRun.EngineVersion.ShouldBe("sim-5");
        RunAssembler.KeyText(Header(null), grossRun.Instruments, grossRun.CalendarExceptions).ShouldContain("\ncosts=none\n");
        RunAssembler.KeyText(Header(costs), costedRun.Instruments, costedRun.CalendarExceptions).ShouldContain("\ncosts=test-synthetic-1|" + TestCostSheet.Sheet.RatesSha256 + "|1000000|standard|0.5|25|0.1\n");
        RunAssembler.KeyText(Header(null), grossRun.Instruments, grossRun.CalendarExceptions).ShouldStartWith("xd-run 5\nengine=sim-5\n");
        costedRun.Totals.Fates.Trades.ShouldBe(2);
    }

    [Fact]
    public void The_run_key_of_two_costed_runs_of_the_same_input_is_equal_and_changes_with_each_of_the_cost_inputs()
    {
        var bars = TwoDays();
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(Rep.Id, "TEST")]);
        string Key(CostContext context)
        {
            var header = new RunHeader(Bare.Record, SimulationEngine.Version, ReplayDays.D1, ReplayDays.D2, ReplayOptions.Default, false, Bare.Fills.ToCanonicalJson(), universe, Rep.DefaultCalendar, CostRecord.Of(context));
            return RunAssembler.Assemble(header, [Costed(bars, ReplayDays.D1, ReplayDays.D2, context)]).RunKey;
        }

        var baseline = Key(Context());

        Key(Context()).ShouldBe(baseline);
        new[] { Key(Context(capital: 900_000m)), Key(Context(maxCostR: 0.2m)), Key(TestCostSheet.Context(profile: SizingProfile.Conservative)), Key(TestCostSheet.Context(sheet: TestCostSheet.WithRounding(ChargeRounding.HalfEven))) }
            .Append(baseline).Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public void The_header_labels_overload_names_the_sheet_in_the_third_place_and_the_old_overload_is_the_gross_one()
    {
        var gross = SimulationHeader.LabelsOf(FillRules.Default);
        var record = CostRecord.Of(Context());

        SimulationHeader.LabelsOf(FillRules.Default, null).ShouldBe(gross);
        gross.ShouldBe(["fills: modelled", "entry limit: +0.1 %", "costs: none", "portfolio limits: not simulated", "sample: in-sample"]);
        SimulationHeader.LabelsOf(FillRules.Default, record).ShouldBe(["fills: modelled", "entry limit: +0.1 %", "costs: sheet test-synthetic-1 (TEST sheet: synthetic rates, not the broker's) (not checked against bills)", "portfolio limits: not simulated", "sample: in-sample"]);
        SimulationHeader.LabelsOf(FillRules.Create(0.1, SlippageConfig.None).Value, null)[0].ShouldBe("fills: exact");
        var sheetRun = CostRecord.Of(TestCostSheet.Context(sheet: CostSheet.Create("dhan-x", "Dhan", "page", "bills", TestCostSheet.Sheet.Schedules).Value));
        SimulationHeader.LabelsOf(FillRules.Default, sheetRun)[2].ShouldBe("costs: sheet dhan-x");
    }

    [Fact]
    public void A_swing_strategy_is_costed_as_delivery_with_the_entry_leg_on_the_earlier_schedule_and_the_exit_leg_on_the_later()
    {
        var profile = SizingProfile.Create("test-wide", 2m, 100m).Value;
        var context = TestCostSheet.Context(1_000_000m, 0.5m, profile);

        var replay = Rep.Replay(Swing, FixtureR.Bars(Wed), Mon, Wed, calendar: DefaultWeek(), costs: context);

        var trade = replay.Trades.ShouldHaveSingleItem();
        var costing = trade.Costing.ShouldNotBeNull();
        costing.Segment.ShouldBe(CostSegment.Delivery);
        trade.TradeDate.ShouldBe(Mon);
        SessionClock.DateOf(trade.ExitEpochSeconds).ShouldBe(Wed);
        costing.EntryLeg.Stt.ShouldBe(Math.Round(costing.EntryTurnover * 0.08m / 100m, 2, MidpointRounding.AwayFromZero), "schedule A: delivery STT 0.08 % on the buy");
        costing.ExitLeg.Stt.ShouldBe(Math.Round(costing.ExitTurnover * 0.09m / 100m, 2, MidpointRounding.AwayFromZero), "schedule B (from 2026-03-03): delivery sell STT 0.09 %");
        costing.EntryLeg.DpCharge.ShouldBe(0m);
        costing.ExitLeg.DpCharge.ShouldBe(10m);
        costing.EntryLeg.Brokerage.ShouldBe(0m, "delivery brokerage is 0 in doc 16");
    }

    [Fact]
    public void The_position_gate_counts_a_swing_trade_that_costing_skipped_and_the_fates_say_so_named_limit_swing_gate_before_costing()
    {
        // Tuesday breaks out of its own opening range at 10:30 while Monday's position (held to Wednesday) is open: the second candidate is a position-open fate with or without costing the first
        var bars = FixtureR.Bars(Fri, "10:30 101.50/103.00/101.50/103.00");
        var kept = Rep.Replay(Swing, bars, Mon, Fri, calendar: DefaultWeek(), costs: TestCostSheet.Context(1_000_000m, 0.10m));
        var skipped = Rep.Replay(Swing, bars, Mon, Fri, calendar: DefaultWeek(), costs: TestCostSheet.Context(1_000_000m, 0.01m));
        var gross = Rep.Replay(Swing, bars, Mon, Fri, calendar: DefaultWeek());

        (kept.Record.Fates.Trades, kept.Record.Fates.SkippedCost, kept.Record.Fates.PositionOpen).ShouldBe((1L, 0L, 1L));
        (skipped.Record.Fates.Trades, skipped.Record.Fates.SkippedCost, skipped.Record.Fates.PositionOpen).ShouldBe((0L, 1L, 1L));
        (gross.Record.Fates.Trades, gross.Record.Fates.PositionOpen).ShouldBe((1L, 1L));
        skipped.Trades.ShouldBeEmpty();
        skipped.Record.Fates.Total.ShouldBe(skipped.Record.Scans.Sum(s => s.Candidates));
        kept.Record.Fates.Total.ShouldBe(kept.Record.Scans.Sum(s => s.Candidates));
        skipped.Record.Fates.Total.ShouldBe(2);
        CostLimits.NotModelled.Select(l => l.Code).ShouldContain("swing_gate_before_costing");
    }

    [Fact]
    public void An_intraday_replay_never_has_a_position_open_fate_with_the_costing_on()
    {
        foreach (var series in SeededReplay.All.Take(40))
        {
            var replay = Rep.Replay(Bare, series.Bars, series.Dates[0], series.Dates[^1], calendar: DefaultWeek(), costs: TestCostSheet.Context(1_000_000m, 100m));

            replay.Record.Fates.PositionOpen.ShouldBe(0, "seed " + series.Seed);
        }
    }

    [Fact]
    public void Costing_does_not_depend_on_the_window_so_the_trades_of_a_shorter_window_are_the_trades_of_a_longer_one()
    {
        foreach (var series in SeededReplay.All.Take(30))
        {
            var context = TestCostSheet.Context(500_000m, 100m);
            var last = series.Dates[^1];
            var from = series.Dates[Math.Min(2, series.Dates.Count - 1)];

            var long_ = Rep.Replay(Swing, series.Bars, series.Dates[0], last, calendar: DefaultWeek(), costs: context);
            var short_ = Rep.Replay(Swing, series.Bars, from, last, calendar: DefaultWeek(), costs: context);

            short_.Trades.ShouldBe(long_.Trades.Where(t => t.TradeDate >= from), "seed " + series.Seed);
            short_.OpenAtEnd.ShouldBe(long_.OpenAtEnd.Where(t => t.TradeDate >= from), "seed " + series.Seed);
        }
    }

    [Fact]
    public void The_handler_path_is_gross_a_replay_input_without_a_context_costs_nothing_and_says_so()
    {
        var input = Rep.Input(ReplayDays.F1(ReplayDays.D1), ReplayDays.D1, ReplayDays.D1);

        input.Costs.ShouldBeNull();
        var replay = InstrumentReplayer.Replay(Bare, input);

        replay.Trades.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            t => t.Costs.ShouldBe(CostBasis.None),
            t => t.Costing.ShouldBeNull());
        replay.Record.Fates.SkippedQuantity.ShouldBe(0);
        replay.Record.Fates.SkippedCost.ShouldBe(0);
    }
}
