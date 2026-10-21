using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 7 of P4-01: thirteen trades with legs whose every figure was computed by hand from the rows below (D6, D7, D10), buys and sells, with and without slippage, two and three exit legs, the stop moved by the rule, a gap through a
/// moved stop overnight, a partial exit and the stop in one minute, a partial that the open through the stop cancels, a NaN level. Every price is a multiple of 0.05 and R is checked to 12 decimals. The frame: BUY at 100.50 (stop 98.00,
/// risk 2.50, one share = 1) or its mirror SELL at 99.50 (stop 102.00), the entry in the 09:45 minute; the tape is written by hand and starts with the decision bar, which closes at the decision instant and is never read.
/// </summary>
public sealed class LegsHandTableTests
{
    private const double Tolerance = 1e-12;

    private static readonly DateOnly Monday = DayBuilder.Monday;

    private static readonly DateOnly Tuesday = Monday.AddDays(1);

    private sealed record L(string At, double Price, double Filled, decimal Share, LegKind Kind, string Reason, DateOnly? Date = null);

    private sealed record Expected(IReadOnlyList<L> Legs, ExitReason Reason, double RExact, double RFilled, double Mae, double Mfe);

    private static ExitTape Tape(DateOnly date, params (string At, double Close, double Low, double High)[] rows) => Tape(rows.Select(r => (date, r.At, r.Close, r.Low, r.High)).ToArray());

    private static ExitTape Tape(params (DateOnly Date, string At, double Close, double Low, double High)[] rows)
    {
        var tape = new ExitTape([TestLadderRule.LowFeature, TestLadderRule.HighFeature]);
        foreach (var (date, at, close, low, high) in rows)
        {
            tape.Append(DayBuilder.Epoch(date, at), close, [low, high]);
        }

        return tape;
    }

    private static EntryCandidate Candidate(Side side) => side == Side.Buy
        ? Cand.Make(Side.Buy, 29, "09:45", 98.00, "09:30", (99.00, 100.50, 99.00, 100.50))
        : Cand.Make(Side.Sell, 29, "09:45", 102.00, "09:30", (101.00, 101.00, 99.50, 99.50));

    private static (Result<WalkOutcome> Walk, IReadOnlyList<Bar> Bars) Walk(Side side, Day day, ExitTape tape, IExitRule rule, bool modelled = false, double? rr = null, bool swing = false)
    {
        var bars = day.Bars();
        var fills = modelled ? FillRules.Default : Cand.NoSlippage;
        var rules = swing
            ? new WalkRules(rr, WalkRules.NoSquareOff, fills, Cand.Tick05, SwingHold.Create(3, TestCalendars.DefaultWeek()).Value)
            : new WalkRules(rr, 915, fills, Cand.Tick05);
        return (TradeWalker.Walk(Candidate(side), bars, rules, new ExitContext(rule, tape)), bars);
    }

    private static void Check(Result<WalkOutcome> walked, Expected expected, Side side)
    {
        walked.IsSuccess.ShouldBeTrue(walked.IsFailure ? walked.Error.Message : string.Empty);
        var outcome = walked.Value;
        var trade = outcome.Trade.ShouldNotBeNull();
        trade.ExitReason.ShouldBe(expected.Reason);
        trade.RExact.ShouldBe(expected.RExact, Tolerance, "RExact");
        trade.RFilled.ShouldBe(expected.RFilled, Tolerance, "RFilled");
        trade.SlippageR.ShouldBe(expected.RFilled - expected.RExact, Tolerance, "SlippageR");
        trade.SlippageR.ShouldBeLessThanOrEqualTo(0.0);
        trade.MaeR.ShouldBe(expected.Mae, Tolerance, "MaeR");
        trade.MfeR.ShouldBe(expected.Mfe, Tolerance, "MfeR");
        trade.Side.ShouldBe(side);
        if (expected.Legs.Count == 0)
        {
            trade.Legs.ShouldBeNull();
            return;
        }

        var legs = trade.Legs.ShouldNotBeNull();
        legs.Select(l => (l.EpochSeconds, l.Price, l.FilledPrice, l.Share, l.Kind, l.Reason))
            .ShouldBe(expected.Legs.Select(l => (DayBuilder.Epoch(l.Date ?? Monday, l.At), l.Price, l.Filled, l.Share, l.Kind, l.Reason)));
        legs.Count(l => l.Kind == LegKind.Exit).ShouldBeGreaterThan(1, "a trade of one entry and one exit is written without legs");
        legs.Where(l => l.Kind == LegKind.Exit).Sum(l => l.Share).ShouldBe(legs.Where(l => l.Kind != LegKind.Exit).Sum(l => l.Share), "the shares out equal the shares in");
        trade.EntryPrice.ShouldBe(legs[0].Price);
        trade.ExitPrice.ShouldBe(legs[^1].Price);
        trade.ExitEpochSeconds.ShouldBe(legs[^1].EpochSeconds);
        outcome.ToCounters().LegsTotal.ShouldBe(legs.Count);
    }

    // ---- the minutes of the buy (rows 1, 3, 7, 8, 11, 13 share the first ones)

    private static Day BuyDay(params string[] rows) => Day.Dense().Start(100.50).Set("09:45 100.50/100.75/100.25/100.50", "09:50 100.50/101.00/100.50/101.00", "09:55 101.00/102.00/101.00/102.00", "09:59 102.00/103.25/102.00/103.25", "10:00 103.25/103.50/103.00/103.25").Set(rows);

    private static Day SellDay(params string[] rows) => Day.Dense().Start(99.50).Set("09:45 99.50/99.75/99.25/99.50", "09:50 99.50/99.50/99.00/99.00", "09:55 99.00/99.00/98.00/98.00", "09:59 98.00/98.00/96.75/96.75", "10:00 96.75/97.00/96.50/96.75").Set(rows);

    private static ExitTape BuyTape() => Tape(Monday, ("09:45", 100.50, 99.00, 100.50), ("10:00", 103.25, 100.25, 103.25), ("10:15", 103.50, 102.00, 104.00));

    private static ExitTape SellTape() => Tape(Monday, ("09:45", 99.50, 99.50, 101.00), ("10:00", 96.75, 96.75, 99.75), ("10:15", 96.50, 96.00, 98.00));

    [Fact]
    public void H1_a_buy_books_half_at_103_25_and_is_stopped_at_the_trailed_102_00()
    {
        // 10:00 bar (low 100.25) moves the stop; its close 103.25 > 100.50 + 2.50: half leaves at the open of 10:00 = 103.25; the 10:15 bar moves the stop to 102.00; 10:20 trades down to 101.50 (open 102.50): stop at 102.00
        var (walked, _) = Walk(Side.Buy, BuyDay("10:20 102.50/102.50/101.50/101.75"), BuyTape(), new TestLadderRule(0.5m));

        // R = (0.5 x (103.25 - 100.50) + 0.5 x (102.00 - 100.50)) / 2.50 = (1.375 + 0.75) / 2.50 = 0.85; MAE (100.50 - 100.25) / 2.50 = 0.1; MFE (103.50 - 100.50) / 2.50 = 1.2
        Check(
            walked,
            new Expected([new("09:45", 100.50, 100.50, 1m, LegKind.Entry, ""), new("10:00", 103.25, 103.25, 0.5m, LegKind.Exit, "LADDER"), new("10:20", 102.00, 102.00, 0.5m, LegKind.Exit, "Stop")], ExitReason.Stop, 0.85, 0.85, 0.1, 1.2),
            Side.Buy);
    }

    [Fact]
    public void H2_the_mirror_sell_gives_the_same_figures()
    {
        var (walked, _) = Walk(Side.Sell, SellDay("10:20 97.50/98.50/97.50/98.25"), SellTape(), new TestLadderRule(0.5m));

        Check(
            walked,
            new Expected([new("09:45", 99.50, 99.50, 1m, LegKind.Entry, ""), new("10:00", 96.75, 96.75, 0.5m, LegKind.Exit, "LADDER"), new("10:20", 98.00, 98.00, 0.5m, LegKind.Exit, "Stop")], ExitReason.Stop, 0.85, 0.85, 0.1, 1.2),
            Side.Sell);
    }

    [Fact]
    public void H3_a_buy_with_slippage_pays_on_every_leg()
    {
        // entry 100.50 + (0.05 + 0.05 x 0.50) = 100.575 up to 100.60 (the limit is 100.60); partial 103.25 - 0.075 = 103.175 down to 103.15; stop 102.00 - (3 x 0.05 + 0.05 x 1.00) = 101.80
        // RFilled = (0.5 x (103.15 - 100.60) + 0.5 x (101.80 - 100.60)) / 2.50 = (1.275 + 0.60) / 2.50 = 0.75; slippage 0.75 - 0.85 = -0.10
        var (walked, _) = Walk(Side.Buy, BuyDay("10:20 102.50/102.50/101.50/101.75"), BuyTape(), new TestLadderRule(0.5m), modelled: true);

        Check(
            walked,
            new Expected([new("09:45", 100.50, 100.60, 1m, LegKind.Entry, ""), new("10:00", 103.25, 103.15, 0.5m, LegKind.Exit, "LADDER"), new("10:20", 102.00, 101.80, 0.5m, LegKind.Exit, "Stop")], ExitReason.Stop, 0.85, 0.75, 0.1, 1.2),
            Side.Buy);
    }

    [Fact]
    public void H4_a_sell_with_slippage_pays_on_every_leg()
    {
        // entry 99.50 - 0.075 = 99.425 down to 99.40, held at the limit 99.45; partial 96.75 + 0.075 up to 96.85; stop 98.00 + 0.20 = 98.20
        // RFilled = -(0.5 x (96.85 - 99.45) + 0.5 x (98.20 - 99.45)) / 2.50 = 1.925 / 2.50 = 0.77; slippage 0.77 - 0.85 = -0.08
        var (walked, _) = Walk(Side.Sell, SellDay("10:20 97.50/98.50/97.50/98.25"), SellTape(), new TestLadderRule(0.5m), modelled: true);

        Check(
            walked,
            new Expected([new("09:45", 99.50, 99.45, 1m, LegKind.Entry, ""), new("10:00", 96.75, 96.85, 0.5m, LegKind.Exit, "LADDER"), new("10:20", 98.00, 98.20, 0.5m, LegKind.Exit, "Stop")], ExitReason.Stop, 0.85, 0.77, 0.1, 1.2),
            Side.Sell);
    }

    [Fact]
    public void H5_three_exit_legs_of_a_buy_a_quarter_a_quarter_and_the_half_at_the_stop()
    {
        // 10:00 bar: a quarter at the open 103.00; 10:15 bar: a quarter at 104.00; 10:30 minute opens 100.00, trades to 97.50: the half leaves at the stop 98.00
        // R = (0.25 x 2.50 + 0.25 x 3.50 + 0.5 x (-2.50)) / 2.50 = 0.25 / 2.50 = 0.10; MAE (100.50 - 98.00) / 2.50 = 1.0; MFE (104.50 - 100.50) / 2.50 = 1.6
        var day = Day.Dense().Start(100.50).Set("09:45 100.50/100.75/100.25/100.50", "10:00 103.00/103.00/102.75/102.75", "10:15 104.00/104.50/104.00/104.25", "10:30 100.00/100.00/97.50/98.00");
        var tape = Tape(Monday, ("09:45", 100.50, 99.00, 100.50), ("10:00", 103.00, 99.00, 103.00), ("10:15", 104.25, 99.00, 104.50), ("10:30", 100.00, 99.00, 100.00));
        var script = new Dictionary<int, ExitAction[]> { [1] = [new(ExitActionKind.ExitShare, 0.25m, "S1")], [2] = [new(ExitActionKind.ExitShare, 0.25m, "S2")] };

        var (walked, _) = Walk(Side.Buy, day, tape, new ScriptedRule(script));

        Check(
            walked,
            new Expected(
                [new("09:45", 100.50, 100.50, 1m, LegKind.Entry, ""), new("10:00", 103.00, 103.00, 0.25m, LegKind.Exit, "S1"), new("10:15", 104.00, 104.00, 0.25m, LegKind.Exit, "S2"), new("10:30", 98.00, 98.00, 0.5m, LegKind.Exit, "Stop")],
                ExitReason.Stop, 0.10, 0.10, 1.0, 1.6),
            Side.Buy);
    }

    [Fact]
    public void H6_the_mirror_sell_of_three_exit_legs()
    {
        var day = Day.Dense().Start(99.50).Set("09:45 99.50/99.75/99.25/99.50", "10:00 97.00/97.25/97.00/97.25", "10:15 96.00/96.00/95.50/95.75", "10:30 100.00/102.50/100.00/102.00");
        var tape = Tape(Monday, ("09:45", 99.50, 101.00, 99.50), ("10:00", 97.00, 101.00, 97.00), ("10:15", 95.75, 101.00, 95.50), ("10:30", 100.00, 101.00, 100.00));
        var script = new Dictionary<int, ExitAction[]> { [1] = [new(ExitActionKind.ExitShare, 0.25m, "S1")], [2] = [new(ExitActionKind.ExitShare, 0.25m, "S2")] };

        var (walked, _) = Walk(Side.Sell, day, tape, new ScriptedRule(script));

        Check(
            walked,
            new Expected(
                [new("09:45", 99.50, 99.50, 1m, LegKind.Entry, ""), new("10:00", 97.00, 97.00, 0.25m, LegKind.Exit, "S1"), new("10:15", 96.00, 96.00, 0.25m, LegKind.Exit, "S2"), new("10:30", 102.00, 102.00, 0.5m, LegKind.Exit, "Stop")],
                ExitReason.Stop, 0.10, 0.10, 1.0, 1.6),
            Side.Sell);
    }

    [Fact]
    public void H7_half_at_103_25_and_the_rest_at_the_target_105_50()
    {
        // reward-risk 2 puts the target at 100.50 + 2 x 2.50 = 105.50; 10:20 opens 105.00 and reaches 106.00: target at 105.50
        // R = (0.5 x 2.75 + 0.5 x 5.00) / 2.50 = 3.875 / 2.50 = 1.55; MAE 0.1; MFE (105.50 - 100.50) / 2.50 = 2.0
        var (walked, _) = Walk(Side.Buy, BuyDay("10:20 105.00/106.00/105.00/105.50"), BuyTape(), new TestLadderRule(0.5m), rr: 2.0);

        Check(
            walked,
            new Expected([new("09:45", 100.50, 100.50, 1m, LegKind.Entry, ""), new("10:00", 103.25, 103.25, 0.5m, LegKind.Exit, "LADDER"), new("10:20", 105.50, 105.50, 0.5m, LegKind.Exit, "Target")], ExitReason.Target, 1.55, 1.55, 0.1, 2.0),
            Side.Buy);
    }

    [Fact]
    public void H8_half_at_103_25_and_the_rest_at_the_square_off_open_103_75()
    {
        // the stop never comes in reach (102.00 from the 10:15 bar); 15:15 opens 103.75: R = (1.375 + 0.5 x 3.25) / 2.50 = 3.0 / 2.50 = 1.2; MFE counts the open of the square-off: (103.75 - 100.50) / 2.50 = 1.3
        var (walked, _) = Walk(Side.Buy, BuyDay("15:15 103.75/104.50/103.00/104.00"), BuyTape(), new TestLadderRule(0.5m));

        Check(
            walked,
            new Expected([new("09:45", 100.50, 100.50, 1m, LegKind.Entry, ""), new("10:00", 103.25, 103.25, 0.5m, LegKind.Exit, "LADDER"), new("15:15", 103.75, 103.75, 0.5m, LegKind.Exit, "SquareOff")], ExitReason.SquareOff, 1.2, 1.2, 0.1, 1.3),
            Side.Buy);
    }

    [Fact]
    public void H9_an_open_through_the_moved_stop_cancels_the_partial_exit_and_the_stop_takes_everything_without_legs()
    {
        // the 10:00 bar moves the stop to 100.25 and asks for the half, but 10:00 opens at 100.00 (through it): the whole position leaves at the open 100.00 (a gap), as one fill
        // R = (100.00 - 100.50) / 2.50 = -0.2; MAE (100.50 - 100.00) / 2.50 = 0.2; MFE (103.25 - 100.50) / 2.50 = 1.1
        var day = Day.Dense().Start(100.50).Set("09:45 100.50/100.75/100.25/100.50", "09:50 100.50/101.00/100.50/101.00", "09:55 101.00/102.00/101.00/102.00", "09:59 102.00/103.25/102.00/103.25", "10:00 100.00/100.00/99.50/99.75");

        var (walked, _) = Walk(Side.Buy, day, BuyTape(), new TestLadderRule(0.5m));

        Check(walked, new Expected([], ExitReason.Stop, -0.2, -0.2, 0.2, 1.1), Side.Buy);
        walked.Value.StopGapExit.ShouldBeTrue();
        walked.Value.Trade!.ExitPrice.ShouldBe(100.00);
    }

    [Fact]
    public void H10_the_half_leaves_at_the_open_and_the_stop_takes_the_other_half_in_the_same_minute()
    {
        // 10:00 opens 103.25 (above the stop 100.25): half at 103.25; the minute trades down to 100.00: the other half at the stop 100.25, in the same minute
        // R = (0.5 x 2.75 + 0.5 x (-0.25)) / 2.50 = 1.25 / 2.50 = 0.5; MAE (100.50 - 100.25) / 2.50 = 0.1; MFE (103.25 - 100.50) / 2.50 = 1.1
        var day = Day.Dense().Start(100.50).Set("09:45 100.50/100.75/100.25/100.50", "09:50 100.50/101.00/100.50/101.00", "09:55 101.00/102.00/101.00/102.00", "09:59 102.00/103.25/102.00/103.25", "10:00 103.25/103.50/100.00/101.00");

        var (walked, _) = Walk(Side.Buy, day, BuyTape(), new TestLadderRule(0.5m));

        Check(
            walked,
            new Expected([new("09:45", 100.50, 100.50, 1m, LegKind.Entry, ""), new("10:00", 103.25, 103.25, 0.5m, LegKind.Exit, "LADDER"), new("10:00", 100.25, 100.25, 0.5m, LegKind.Exit, "Stop")], ExitReason.Stop, 0.5, 0.5, 0.1, 1.1),
            Side.Buy);
    }

    [Fact]
    public void H11_a_bar_whose_low_is_not_ready_moves_no_stop()
    {
        // NaN low and high at 10:00 and 10:15: no new level, the stop stays 98.00; the close still asks for the half; 10:20 opens 99.00 and trades to 97.00: the half at the stop 98.00
        // R = (0.5 x 2.75 + 0.5 x (-2.50)) / 2.50 = 0.125 / 2.50 = 0.05; MAE 1.0; MFE (103.50 - 100.50) / 2.50 = 1.2
        var tape = Tape(Monday, ("09:45", 100.50, 99.00, 100.50), ("10:00", 103.25, double.NaN, double.NaN), ("10:15", 103.50, double.NaN, double.NaN));

        var (walked, _) = Walk(Side.Buy, BuyDay("10:20 99.00/99.00/97.00/98.00"), tape, new TestLadderRule(0.5m));

        Check(
            walked,
            new Expected([new("09:45", 100.50, 100.50, 1m, LegKind.Entry, ""), new("10:00", 103.25, 103.25, 0.5m, LegKind.Exit, "LADDER"), new("10:20", 98.00, 98.00, 0.5m, LegKind.Exit, "Stop")], ExitReason.Stop, 0.05, 0.05, 1.0, 1.2),
            Side.Buy);
    }

    [Fact]
    public void H12_a_level_computed_on_the_last_bar_of_monday_acts_on_the_first_minute_of_tuesday_and_a_gap_through_it_fills_at_the_open()
    {
        // swing: half at 10:00 Monday (103.25); the bar 15:15 to 15:29 closes at 15:30 with low 102.00: the stop 102.00 is in force from Tuesday 09:15, which opens 101.00: the half leaves at the open
        // R = (0.5 x 2.75 + 0.5 x 0.50) / 2.50 = 1.625 / 2.50 = 0.65; MAE 0.1; MFE (103.50 - 100.50) / 2.50 = 1.2
        var monday = Day.Dense(Monday).Start(100.50).Set("09:45 100.50/100.75/100.25/100.50", "09:50 100.50/101.00/100.50/101.00", "09:55 101.00/102.00/101.00/102.00", "09:59 102.00/103.25/102.00/103.25", "10:00 103.25/103.50/103.00/103.25");
        var tuesday = Day.After(monday, Tuesday).Set("09:15 101.00/101.50/100.75/101.25");
        var tape = Tape((Monday, "09:45", 100.50, 99.00, 100.50), (Monday, "10:00", 103.25, 100.25, 103.25), (Monday, "15:30", 103.25, 102.00, 103.50));
        var bars = monday.Bars().Concat(tuesday.Bars()).ToList();
        var rules = new WalkRules(null, WalkRules.NoSquareOff, Cand.NoSlippage, Cand.Tick05, SwingHold.Create(3, TestCalendars.DefaultWeek()).Value);

        var walked = TradeWalker.Walk(Candidate(Side.Buy), bars, rules, new ExitContext(new TestLadderRule(0.5m), tape));

        Check(
            walked,
            new Expected([new("09:45", 100.50, 100.50, 1m, LegKind.Entry, ""), new("10:00", 103.25, 103.25, 0.5m, LegKind.Exit, "LADDER"), new("09:15", 101.00, 101.00, 0.5m, LegKind.Exit, "Stop", Tuesday)], ExitReason.Stop, 0.65, 0.65, 0.1, 1.2),
            Side.Buy);
        walked.Value.StopGapExit.ShouldBeTrue();
        walked.Value.OvernightStopGap.ShouldBeTrue();
        (walked.Value.Trade!.SessionsHeld, walked.Value.Trade.SessionsUnseen).ShouldBe((2, 0));
    }

    [Fact]
    public void H13_a_level_moved_by_the_bar_is_not_in_force_inside_the_minutes_of_that_bar()
    {
        // the 10:00 bar has low 100.00 and the minute 09:59 inside it touches 100.00; the stop moves to 100.00 only from 10:00, so 09:59 does not stop the trade; the half leaves at 103.25 and the rest at the square-off open 103.25
        // R = (0.5 x 2.75 + 0.5 x 2.75) / 2.50 = 1.1; MAE (100.50 - 100.00) / 2.50 = 0.2; MFE 1.2
        var day = Day.Dense().Start(100.50).Set("09:45 100.50/100.75/100.25/100.50", "09:50 100.50/101.00/100.50/101.00", "09:55 101.00/102.00/101.00/102.00", "09:59 102.00/103.25/100.00/103.25", "10:00 103.25/103.50/103.00/103.25");
        var tape = Tape(Monday, ("09:45", 100.50, 99.00, 100.50), ("10:00", 103.25, 100.00, 103.25));

        var (walked, _) = Walk(Side.Buy, day, tape, new TestLadderRule(0.5m));

        Check(
            walked,
            new Expected([new("09:45", 100.50, 100.50, 1m, LegKind.Entry, ""), new("10:00", 103.25, 103.25, 0.5m, LegKind.Exit, "LADDER"), new("15:15", 103.25, 103.25, 0.5m, LegKind.Exit, "SquareOff")], ExitReason.SquareOff, 1.1, 1.1, 0.2, 1.2),
            Side.Buy);
    }

    [Fact]
    public void A_rule_that_asks_for_nothing_gives_the_trade_a_walk_without_a_rule_gives_and_no_legs()
    {
        var day = BuyDay("10:20 102.50/102.50/97.50/98.00");
        var silent = new ScriptedRule(new Dictionary<int, ExitAction[]>());
        var bars = day.Bars();
        var rules = new WalkRules(null, 915, Cand.NoSlippage, Cand.Tick05);

        var with = TradeWalker.Walk(Candidate(Side.Buy), bars, rules, new ExitContext(silent, BuyTape()));
        var without = TradeWalker.Walk(Candidate(Side.Buy), bars, rules);

        with.Value.ShouldBe(without.Value);
        with.Value.Trade!.Legs.ShouldBeNull();
    }
}
