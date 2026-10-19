using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>
/// Criteria 3 to 10 of P3-05: the hand-calculated days V1 to V7 of D9, through the real compiler, frame, scanner and <see cref="Simulator"/> (nothing mocked). Fills are exact (<see cref="SlippageConfig.None"/>, the 0.1 % limit, tick 0.05, no costs)
/// except in the two slippage rows of criterion 5b.
/// </summary>
public sealed class VwapPullbackDayTests
{
    private static readonly DateOnly Date = VwapDays.Date;

    private static readonly Simulator Exact = Sim.Create(VwapPullback.Create(), fills: Cand.NoSlippage);

    private static DayScan Scan(Bar[] bars, Side side = Side.Buy, double pullAtr = 0.5) => DayScan.OfDay(VwapPullback.Create(pullAtr), side, bars, Date);

    private static SimTrade OneTrade(SimulationResult result)
    {
        result.Trades.Count.ShouldBe(1);
        result.OpenAtEnd.ShouldBeEmpty();
        return result.Trades[0];
    }

    private static string Clock(long epoch) => Render.Instant(epoch)[11..];

    private static void ShouldBeNear(double actual, double expected) => actual.ShouldBe(expected, 1e-3);

    [Fact]
    public void The_prefixes_have_the_trend_values_of_D9_by_plain_arithmetic_and_an_atr_of_exactly_one()
    {
        var up = ReferenceLoop.TrendAtEnd(BandedDays.Up(Date));
        var down = ReferenceLoop.TrendAtEnd(BandedDays.Down(Date));
        var sixBars = ReferenceLoop.TrendAtEnd(BandedDays.Up(Date), dropStub: true);

        (up.DailyClose, up.DailyEma).ShouldBe((110.50m, 105.75m));
        (up.HourlyFast - up.HourlySlow).ShouldBe(1.054m, 0.001m);
        (down.DailyClose, down.DailyEma).ShouldBe((110.50m, 115.25m));
        (down.HourlyFast - down.HourlySlow).ShouldBe(-1.054m, 0.001m);
        (sixBars.HourlyFast - sixBars.HourlySlow).ShouldBe(1.235m, 0.001m);
        BandedDays.Up(Date).Length.ShouldBe(21 * 375);
    }

    [Fact]
    public void V1_gives_the_candidate_of_the_spec_with_the_trace_of_five_true_items()
    {
        var scan = Scan(VwapDays.V1());

        var candidate = scan.Scanner.Candidates.Single();
        Render.Candidate(candidate, withTrace: false).ShouldBe(Expect.Candidate(Side.Buy, 7_964, "10:45", 112.50, "10:30", 113.00, 114.00, 113.00, 113.75, 15, true, "VWAP_PULLBACK", Date));
        candidate.Trace.Items.Select(i => i.Path).ShouldBe(["buy.setup[0]", "buy.setup[1]", "buy.trigger[0]", "buy.trigger[1]", "buy.trigger[2]"]);
        candidate.Trace.Items.ShouldAllBe(i => i.Truth == TraceTruth.True);
        var vwap = candidate.Trace.Items[4].Values.Single(v => v.Feature.StartsWith("vwap", StringComparison.Ordinal)).Lags;
        ShouldBeNear(vwap[0], 112.5694);
        ShouldBeNear(vwap[1], 112.3667);
        ShouldBeNear(vwap[2], 112.2083);
        candidate.Trace.Items[4].Values.Single(v => v.Feature.StartsWith("low", StringComparison.Ordinal)).Lags.ShouldBe([113.00, 112.50, 112.75]);
        candidate.Trace.Items[4].Values.Single(v => v.Feature.StartsWith("atr", StringComparison.Ordinal)).Lags[0].ShouldBe(1.0, 1e-9);
        candidate.Trace.Items[1].Values.Single(v => v.Feature.StartsWith("close@1D", StringComparison.Ordinal)).Lags.ShouldBe([110.50]);
    }

    [Fact]
    public void V1_walks_to_the_target_at_two_r()
    {
        var result = Sim.Run(Exact, VwapDays.V1(), Scan_Instrument);

        var trade = OneTrade(result);
        (trade.EntryPrice, trade.StopPrice, trade.TargetPrice, trade.ExitPrice, trade.ExitReason).ShouldBe((113.75, 112.50, 116.25, 116.25, ExitReason.Target));
        (Clock(trade.EntryEpochSeconds), Clock(trade.ExitEpochSeconds)).ShouldBe(("10:45", "11:30"));
        trade.EntryFill.ShouldBe(EntryFill.AtOpen);
        (trade.RExact, trade.MaeR, trade.MfeR).ShouldBe((2.0, 0.4, 2.0));
        var walk = result.Outcomes.Single().Walk;
        (walk.StopGapExit, walk.TargetGapExit, walk.AmbiguousMinute).ShouldBe((false, false, false));
        result.Scans[0].LaterTriggers.ShouldBe(0);
    }

    [Fact]
    public void V2_walks_to_the_stop_and_the_pattern_is_not_armed_again()
    {
        var result = Sim.Run(Exact, VwapDays.V2(), Scan_Instrument);

        var trade = OneTrade(result);
        (trade.EntryPrice, trade.StopPrice, trade.TargetPrice, trade.ExitPrice, trade.ExitReason).ShouldBe((113.75, 112.50, 116.25, 112.50, ExitReason.Stop));
        (Clock(trade.EntryEpochSeconds), Clock(trade.ExitEpochSeconds)).ShouldBe(("10:45", "11:05"));
        (trade.RExact, trade.MaeR, trade.MfeR).ShouldBe((-1.0, 1.0, 0.2));
        result.Outcomes.Count.ShouldBe(1);
    }

    [Fact]
    public void V3_the_down_trend_prefix_blocks_the_buy_at_the_daily_setup_and_the_sell_has_no_trigger()
    {
        var bars = VwapDays.V3();

        var buy = Scan(bars);

        buy.Scanner.Candidates.ShouldBeEmpty();
        buy.Conditions["buy.setup[1]"].ShouldBe((0, 19, 0, 0));
        buy.Conditions["buy.trigger[0]"].True.ShouldBe(5);
        buy.Conditions["buy.trigger[1]"].True.ShouldBe(19);
        buy.Conditions["buy.trigger[2]"].True.ShouldBe(1);
        (buy.Candidates, buy.LaterTriggers).ShouldBe((0, 0));
        var sell = Scan(bars, Side.Sell);
        sell.Conditions["sell.setup[1]"].ShouldBe((19, 0, 0, 0));
        sell.Scanner.Candidates.ShouldBeEmpty();
        Sim.Run(Exact, bars, Scan_Instrument).Outcomes.ShouldBeEmpty();
    }

    [Fact]
    public void V4_a_shallow_pullback_is_not_to_vwap_at_half_an_atr_and_is_at_two_atrs_on_the_last_entry_minute()
    {
        var bars = VwapDays.V4();

        var shallow = Scan(bars);
        var deep = Scan(bars, pullAtr: 2.0);

        shallow.Scanner.Candidates.ShouldBeEmpty();
        shallow.Conditions["buy.trigger[0]"].True.ShouldBe(5);
        shallow.Conditions["buy.trigger[1]"].True.ShouldBeGreaterThanOrEqualTo(1);
        shallow.Conditions["buy.trigger[2]"].True.ShouldBe(0, "the dip's low is more than 0.5 ATR above VWAP");
        deep.Conditions["buy.trigger[2]"].True.ShouldBe(1);
        var candidate = deep.Scanner.Candidates.Single();
        Render.Candidate(candidate, withTrace: false).ShouldBe(Expect.Candidate(Side.Buy, 8_189, "14:30", 112.50, "14:15", 113.00, 114.00, 113.00, 113.75, 15, true, "VWAP_PULLBACK", Date));
        var result = Sim.Run(Sim.Create(VwapPullback.Create(2.0), fills: Cand.NoSlippage), bars, Scan_Instrument);
        var trade = OneTrade(result);
        (trade.EntryPrice, trade.ExitPrice, trade.ExitReason, trade.RExact).ShouldBe((113.75, 113.75, ExitReason.SquareOff, 0.0));
        (Clock(trade.EntryEpochSeconds), Clock(trade.ExitEpochSeconds)).ShouldBe(("14:30", "15:15"));
    }

    [Fact]
    public void V4_one_bar_later_the_trigger_is_decided_at_14_45_outside_the_window_for_any_pull()
    {
        var bars = VwapDays.V4(extraFlatBars: 1);

        foreach (var pull in new[] { 0.5, 2.0, 3.0 })
        {
            var scan = Scan(bars, pullAtr: pull);

            scan.Scanner.Candidates.ShouldBeEmpty("pull " + pull);
            scan.OutOfWindowSteps.ShouldBe(6);
            (scan.DecisionSteps, scan.WindowSteps).ShouldBe((25, 19));
        }
    }

    [Fact]
    public void V5_the_sell_mirror_is_written_out_and_walks_to_the_target()
    {
        var bars = VwapDays.V5();

        var sell = Scan(bars, Side.Sell);
        var buy = Scan(bars);

        buy.Scanner.Candidates.ShouldBeEmpty();
        Render.Candidate(sell.Scanner.Candidates.Single(), withTrace: false)
            .ShouldBe(Expect.Candidate(Side.Sell, 7_964, "10:45", 108.50, "10:30", 108.00, 108.00, 107.00, 107.25, 15, true, "VWAP_PULLBACK", Date));
        var trade = OneTrade(Sim.Run(Exact, bars, Scan_Instrument));
        (trade.Side, trade.EntryPrice, trade.StopPrice, trade.TargetPrice, trade.ExitPrice, trade.ExitReason).ShouldBe((Side.Sell, 107.25, 108.50, 104.75, 104.75, ExitReason.Target));
        trade.EntryFill.ShouldBe(EntryFill.AtOpen);
        Clock(trade.ExitEpochSeconds).ShouldBe("11:30");
        (trade.RExact, trade.MaeR, trade.MfeR).ShouldBe((2.0, 0.4, 2.0));
    }

    [Fact]
    public void V5_the_buy_and_the_sell_of_one_date_would_both_be_kept()
    {
        // two independent scanners, one per side: the result of a run lists the candidates of both sides, Buy before Sell
        var result = Sim.Run(Exact, VwapDays.V5(), Scan_Instrument);

        result.Scans.Select(s => s.Side).ShouldBe([Side.Buy, Side.Sell]);
        result.Scans[0].Candidates.ShouldBe(0);
        result.Scans[1].Candidates.ShouldBe(1);
    }

    [Fact]
    public void Criterion_5b_default_slippage_on_V2_costs_a_stop_and_V5_a_target()
    {
        var v2 = OneTrade(Sim.Run(VwapPullback.Create(), VwapDays.V2()));
        var v5 = OneTrade(Sim.Run(VwapPullback.Create(), VwapDays.V5()));

        (v2.EntryFilledPrice, v2.ExitFilledPrice).ShouldBe((113.80, 112.25));
        v2.RFilled.ShouldBe(-1.24, 1e-9);
        v2.SlippageR.ShouldBe(-0.24, 1e-9);
        v2.RExact.ShouldBe(-1.0);
        (v5.EntryFilledPrice, v5.ExitFilledPrice).ShouldBe((107.20, 104.75));
        v5.RFilled.ShouldBe(1.96, 1e-9);
        v5.SlippageR.ShouldBe(-0.04, 1e-9);
        v5.RExact.ShouldBe(2.0);
        (v2.ExitReason, v5.ExitReason).ShouldBe((ExitReason.Stop, ExitReason.Target));
    }

    [Fact]
    public void V6_a_replay_with_five_prior_sessions_shows_the_cold_start_in_the_counters()
    {
        var bars = VwapDays.V6();

        var scan = DayScan.OfDay(VwapPullback.Create(), Side.Buy, bars, Date);

        scan.Scanner.Candidates.ShouldBeEmpty();
        scan.Conditions["buy.setup[0]"].ShouldBe((0, 0, 0, 19));
        scan.Conditions["buy.setup[1]"].ShouldBe((0, 0, 0, 19));
        scan.FullyReadySteps.ShouldBe(0);
    }

    [Fact]
    public void V7_the_first_two_bars_of_a_session_cannot_be_known_for_the_lagged_items()
    {
        var bars = VwapDays.V7();

        var scan = DayScan.OfDay(VwapDays.AllDay(), Side.Buy, bars, Date);

        scan.Conditions["buy.trigger[1]"].Unknown.ShouldBe(2);

        // Spec AC8 says trigger[2] is Unknown at bars 1 and 2 as well. It is not: the lower-low conjunct reads `low`, whose history is continuous across sessions, and is known and False there; a False conjunct decides an `and` whatever the
        // other one is, so the item is False at both bars (it is never True before bar 3). The deviation is reported; nothing can fire from bars 1 and 2 either way.
        scan.Conditions["buy.trigger[2]"].Unknown.ShouldBe(0);
        scan.Conditions["buy.trigger[0]"].Unknown.ShouldBe(0);
        scan.WindowSteps.ShouldBe(25);
        scan.Scanner.Candidates.Single().DecisionBarIndex.ShouldBe(7_964);
    }

    [Fact]
    public void V7_a_positional_lag_across_the_session_boundary_is_not_what_the_engine_gives()
    {
        // Plain arithmetic: read by position across the session boundary, bar 1 would see close[1] = 110.50 (the last close of the prefix) and vwap[1] = the typical price of the prefix's last bar = (111.00 + 110.00 + 110.50) / 3 = 110.50,
        // so the first conjunct would be KNOWN and False (110.50 > 110.50); the engine says Unknown, because vwap keeps the history of the session only.
        var positional = 110.50 > (111.00 + 110.00 + 110.50) / 3;
        positional.ShouldBeFalse();

        var scan = DayScan.OfDay(VwapDays.AllDay(), Side.Buy, VwapDays.V7(), Date);

        // the engine keeps the history of the session only (ADR 0035): the item is Unknown on bars 1 and 2
        scan.Conditions["buy.trigger[1]"].Unknown.ShouldBe(2);
    }

    [Fact]
    public void The_counters_of_V1_are_those_of_the_spec()
    {
        var bars = VwapDays.V1();

        var scan = Scan(bars);

        scan.Scanner.Counters.Bars.ShouldBe(8_250);
        (scan.DecisionSteps, scan.StaleBucketSteps, scan.OutOfWindowSteps, scan.WindowSteps, scan.FullyReadySteps).ShouldBe((25, 0, 6, 19, 19));
        (scan.Candidates, scan.LaterTriggers, scan.BlockedByInvalidation, scan.InvalidatedDays, scan.StopNotReady, scan.UnknownInvalidation).ShouldBe((1, 0, 0, 0, 0, 0));
        scan.Conditions["buy.setup[0]"].ShouldBe((19, 0, 0, 0));
        scan.Conditions["buy.setup[1]"].ShouldBe((19, 0, 0, 0));
        scan.Conditions["buy.trigger[0]"].ShouldBe((5, 14, 0, 0));
        scan.Conditions["buy.trigger[1]"].ShouldBe((19, 0, 0, 0));
        scan.Conditions["buy.trigger[2]"].ShouldBe((1, 18, 0, 0));
        var sell = Scan(bars, Side.Sell);
        sell.Conditions["sell.setup[1]"].ShouldBe((0, 19, 0, 0));
        sell.Scanner.Candidates.ShouldBeEmpty();
        Identities.ShouldHold(scan.Scanner);
        Identities.ShouldHold(sell.Scanner);
    }

    [Fact]
    public void The_engine_stays_sim_5_and_the_strategy_creates_a_simulator_without_problems()
    {
        SimulationEngine.Version.ShouldBe("sim-5");
        var build = Simulator.Create(VwapPullback.Create(), Timeframe.M15);
        build.Problems.ShouldBeEmpty();
        build.Simulator.ShouldNotBeNull();
    }

    private static InstrumentRef Scan_Instrument => Support.Scan.Instrument;
}
