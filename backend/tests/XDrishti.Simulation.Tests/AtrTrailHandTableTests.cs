using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 2 of P4-03: twenty-two <c>ATR_TRAIL</c> rows computed by hand from D1 to D3, buys and sells, intraday and swing, activation 0 and above 0, an activation on a close and a touch that closes back, an ATR that is not a number, a looser
/// level that is ignored, a level the price has already passed (a gap), the initial stop before any level, the square-off, <c>MAX_HOLD</c> and the end of the data after a level, a daily bar, and two rows with slippage. The frame is that of
/// <see cref="TrailRow"/>: entry 100.00, stop 99.00 (a sell 101.00), risk 1.00, so R is the price difference. A level is <c>H − mult × ATR</c> with H the highest high since the fill minute, read at the first minute at or after the close of the bar.
/// Row A23 is the <c>time_stop</c> overlay of P4-02 (ADR 0050) after a level: the trail and the overlay run together.
/// </summary>
public sealed class AtrTrailHandTableTests
{
    private static TrailRow Row(string name, Side side, string[] minutes, TrailBar[] tape, TrailWant want, double multiple, double activate, string[]? tuesday = null, int hold = 0, bool modelled = false, string? endsAfter = null) =>
        new(name, side, minutes, tape, want, tuesday, hold, modelled, endsAfter, multiple, activate);

    private static TrailBar B(string at, double close, double atr) => new(at, close, atr);

    public static TheoryData<TrailRow> Rows { get; } =
    [
        // A1 (D3): bar 1 closes 100.40 (progress 0.4, not active); bar 2 closes 101.20 (progress 1.2: active), H = 101.60, level 101.60 - 2 x 0.50 = 100.60 from 10:15; 10:20 trades down to 100.60: out at 100.60. R 0.60, MAE 0, MFE 1.60
        Row("A1 the worked example of D3", Side.Buy, ["09:59 100.00/100.40/100.00/100.40", "10:05 100.40/101.60/100.40/101.20", "10:20 101.20/101.20/100.60/100.70"], [B("10:00", 100.40, 0.5), B("10:15", 101.20, 0.5)],
            new(ExitReason.Stop, "10:20", 100.60, 0.60, 0, 1.60), 2.0, 1.0),

        // A2: the mirror SELL: H is the lowest low 98.40, level 98.40 + 1.00 = 99.40 (below the stop 101.00, so tighter); 10:20 trades up to 99.40
        Row("A2 the mirror sell of A1", Side.Sell, ["09:59 100.00/100.00/99.60/99.60", "10:05 99.60/99.60/98.40/98.80", "10:20 98.80/99.40/98.80/99.30"], [B("10:00", 99.60, 0.5), B("10:15", 98.80, 0.5)],
            new(ExitReason.Stop, "10:20", 99.40, 0.60, 0, 1.60), 2.0, 1.0),

        // A3: a bar touches 101.30 (progress 1.3) and closes at 100.50, the next closes 100.80: never active, no level; 10:20 trades down to the initial stop 99.00. R -1.00; MAE 1.00 (the exit price), MFE 1.30
        Row("A3 a touch that closes back does not activate", Side.Buy, ["09:50 100.00/101.30/100.00/100.50", "10:10 100.50/100.80/100.50/100.80", "10:20 100.80/100.80/98.80/99.00"], [B("10:00", 100.50, 0.5), B("10:15", 100.80, 0.5)],
            new(ExitReason.Stop, "10:20", 99.00, -1.00, 1.00, 1.30), 2.0, 1.0),

        // A4: activate_at_rr 0 is active from the first bar although it closed below the entry (99.80): H 100.20, level 100.20 - 1.5 x 0.40 = 99.60 from 10:00; 10:05 low 99.60: out at 99.60. R -0.40, MAE 0.40, MFE 0.20
        Row("A4 activation 0 trails a first bar that closed below the entry", Side.Buy, ["09:50 100.00/100.20/99.80/99.80", "10:05 99.80/99.80/99.60/99.70"], [B("10:00", 99.80, 0.4)],
            new(ExitReason.Stop, "10:05", 99.60, -0.40, 0.40, 0.20), 1.5, 0),

        // A5: the ATR is not a number on both bars: no level, the stop stays 99.00, the position leaves at the square-off open 100.30. R 0.30, MAE 0, MFE 0.50
        Row("A5 an ATR that is not a number gives no level", Side.Buy, ["10:05 100.00/100.50/100.00/100.30"], [B("10:00", 100.30, double.NaN), B("10:15", 100.50, double.NaN)],
            new(ExitReason.SquareOff, "15:15", 100.30, 0.30, 0, 0.50), 2.0, 0),

        // A6: bar 1: H 100.40, level 100.40 - 1.50 = 98.90 is looser than 99.00: ignored and counted; bar 2: H 101.00, level 99.50 is tighter; 10:20 low 99.40: out at 99.50. R -0.50, MAE 0.50, MFE 1.00
        Row("A6 a looser level is ignored and counted", Side.Buy, ["09:50 100.00/100.40/100.00/100.30", "10:05 100.30/101.00/100.30/101.00", "10:20 101.00/101.00/99.40/99.60"], [B("10:00", 100.30, 0.5), B("10:15", 101.00, 0.5)],
            new(ExitReason.Stop, "10:20", 99.50, -0.50, 0.50, 1.00, Ignored: 1), 3.0, 0),

        // A7: H 101.00, level 101.00 - 0.50 = 100.50 from 10:00, but the 10:00 minute opens at 100.20: the level has been passed, the exit is the open (a gap). R 0.20, MAE 0, MFE 1.00
        Row("A7 a level the price has passed leaves at the open", Side.Buy, ["09:50 100.00/101.00/100.00/101.00", "10:00 100.20/100.20/100.00/100.00"], [B("10:00", 101.00, 0.5)],
            new(ExitReason.Stop, "10:00", 100.20, 0.20, 0, 1.00, StopGap: true), 1.0, 0),

        // A8: activate_at_rr 2: no close reaches 102.00, so the trail never starts and the initial stop is the only one; 11:00 opens 98.80, through 99.00: out at the open. R -1.20, MAE 1.20 (the exit price), MFE 1.10
        Row("A8 before any level the initial stop is the stop", Side.Buy, ["09:50 100.00/100.60/100.00/100.50", "10:10 100.50/101.10/100.50/101.00", "10:20 101.00/101.00/100.80/100.80", "11:00 98.80/98.90/98.50/98.60"],
            [B("10:00", 100.50, 0.5), B("10:15", 101.00, 0.5), B("10:30", 100.80, 0.5)], new(ExitReason.Stop, "11:00", 98.80, -1.20, 1.20, 1.10, StopGap: true), 2.0, 2.0),

        // A9: level 100.60 - 2 x 0.25 = 100.10 from 10:00 and again from 10:15 (equal, not looser); never touched, the square-off open is 100.60. R 0.60, MAE 0, MFE 0.60
        Row("A9 the square-off after a level", Side.Buy, ["09:50 100.00/100.60/100.00/100.60"], [B("10:00", 100.60, 0.25), B("10:15", 100.60, 0.25)],
            new(ExitReason.SquareOff, "15:15", 100.60, 0.60, 0, 0.60), 2.0, 0),

        // A10: swing, hold 2: level 100.10 from Monday 10:00, never touched; Tuesday 13:00 goes to 101.20 and closes the day at 101.00: MAX_HOLD at the close of 15:29. R 1.00, MAE 0, MFE 1.20 (the high of the day, above the close)
        Row("A10 MAX_HOLD after a level", Side.Buy, ["09:50 100.00/100.60/100.00/100.60"], [B("10:00", 100.60, 0.25)],
            new(ExitReason.MaxHold, "15:29", 101.00, 1.00, 0, 1.20, Sessions: 2, Day: 1), 2.0, 0, ["13:00 100.60/101.20/100.60/101.00"], 2),

        // A12: E = 1D (the bar of Monday closes at 15:30): H 101.00, level 101.00 - 0.50 = 100.50 from Tuesday 09:15, which opens 100.40: a gap through it. R 0.40, MAE 0, MFE 1.00
        Row("A12 a daily bar gives a level that Tuesday's open has passed", Side.Buy, ["10:00 100.00/101.00/100.00/100.80"], [B("15:30", 100.80, 0.5)],
            new(ExitReason.Stop, "09:15", 100.40, 0.40, 0, 1.00, StopGap: true, Sessions: 2, Day: 1), 1.0, 0, ["09:15 100.40/100.40/100.30/100.30"], 3),

        // A13: a 15-minute bar closing at 15:30 on Monday: H 100.80, level 100.80 - 0.50 = 100.30, in force from Tuesday 09:15 (open 100.80): Tuesday low 100.20 touches it: out at 100.30. R 0.30, MAE 0, MFE 0.80
        Row("A13 a level across the session break acts on the next session", Side.Buy, ["10:00 100.00/100.80/100.00/100.80"], [B("15:30", 100.80, 0.25)],
            new(ExitReason.Stop, "09:15", 100.30, 0.30, 0, 0.80, Sessions: 2, Day: 1), 2.0, 0, ["09:15 100.80/100.80/100.20/100.40"], 3),

        // A14: the fill minute opens 100.40 above the limit 100.10 and trades back: AtLimit at 100.10, risk 1.10. H counts the fill minute (high 100.50): level 100.50 - 0.20 = 100.30 from 10:00, where the flat minute opens at 100.30 (not below): out at 100.30.
        // R 0.20 / 1.10; MAE (100.10 - 100.05) / 1.10 (only the adverse side of an at-limit fill minute counts); MFE (100.30 - 100.10) / 1.10 from the minutes after it
        Row("A14 the fill bar with a gap-up open", Side.Buy, ["09:45 100.40/100.50/100.05/100.30"], [B("10:00", 100.30, 0.20)],
            new(ExitReason.Stop, "10:00", 100.30, 0.20 / 1.10, 0.05 / 1.10, 0.20 / 1.10, Entry: 100.10), 1.0, 0),

        // A15: the list ends with the fill minute: the position leaves at its close 100.00. R 0
        Row("A15 a one-minute position", Side.Buy, [], [B("10:00", 100.00, 0.5)], new(ExitReason.EndOfData, "09:45", 100.00, 0, 0, 0), 1.0, 0, endsAfter: "09:45"),

        // A16: progress exactly 1.0 activates (>=): H 101.20, level 101.20 - 0.50 = 100.70; 10:10 low 100.60: out at 100.70. R 0.70, MAE 0, MFE 1.20
        Row("A16 a close exactly at activate_at_rr activates", Side.Buy, ["09:50 100.00/101.20/100.00/101.00", "10:10 101.00/101.00/100.60/100.80"], [B("10:00", 101.00, 0.25)],
            new(ExitReason.Stop, "10:10", 100.70, 0.70, 0, 1.20), 2.0, 1.0),

        // A17: closes 100.50, 100.90, 101.20: active at bar 3; the anchor is the entry, not the activation: H 101.50 (reached in bar 2), level 101.50 - 0.50 = 101.00 from 10:30; 10:35 low 100.90: out at 101.00. R 1.00, MAE 0, MFE 1.50
        Row("A17 the anchor is the whole run since the entry", Side.Buy, ["09:50 100.00/100.50/100.00/100.50", "10:05 100.50/101.50/100.50/100.90", "10:20 100.90/101.20/100.90/101.20", "10:35 101.20/101.20/100.90/101.00"],
            [B("10:00", 100.50, 0.5), B("10:15", 100.90, 0.5), B("10:30", 101.20, 0.5)], new(ExitReason.Stop, "10:35", 101.00, 1.00, 0, 1.50), 1.0, 1.0),

        // A18: A1 with slippage: entry 100.00 + 0.05 = 100.05; the stop at 100.60 in a minute of range 0.60 pays (1 + 2) x 0.05 + 0.05 x 0.60 = 0.18: 100.42 down to 100.40. RFilled (100.40 - 100.05) / 1.00 = 0.35
        Row("A18 slippage on the trailed stop", Side.Buy, ["09:59 100.00/100.40/100.00/100.40", "10:05 100.40/101.60/100.40/101.20", "10:20 101.20/101.20/100.60/100.70"], [B("10:00", 100.40, 0.5), B("10:15", 101.20, 0.5)],
            new(ExitReason.Stop, "10:20", 100.60, 0.60, 0, 1.60, RFilled: 0.35), 2.0, 1.0, modelled: true),

        // A19: the mirror of A6: bar 1 level 99.60 + 1.50 = 101.10 is looser than the stop 101.00: ignored; bar 2 H 99.00, level 100.50 is tighter; 10:20 high 100.60: out at 100.50. R -0.50, MAE 0.50, MFE 1.00
        Row("A19 a sell ignores a looser level", Side.Sell, ["09:50 100.00/100.00/99.60/99.70", "10:05 99.70/99.70/99.00/99.00", "10:20 99.00/100.60/99.00/100.40"], [B("10:00", 99.70, 0.5), B("10:15", 99.00, 0.5)],
            new(ExitReason.Stop, "10:20", 100.50, -0.50, 0.50, 1.00, Ignored: 1), 3.0, 0),

        // A20: a sell on the daily bar: H (lowest low) 99.00, level 99.00 + 0.50 = 99.50; Tuesday 09:15 opens 99.60, above it: the open. R 0.40, MAE 0, MFE 1.00
        Row("A20 a sell on a daily bar gaps through its level", Side.Sell, ["10:00 100.00/100.00/99.00/99.20"], [B("15:30", 99.20, 0.5)],
            new(ExitReason.Stop, "09:15", 99.60, 0.40, 0, 1.00, StopGap: true, Sessions: 2, Day: 1), 1.0, 0, ["09:15 99.60/99.70/99.60/99.70"], 3),

        // A21: bar 1 level 100.60 - 0.40 = 100.20; the ATR of bar 2 is not a number: the stop stays 100.20; 10:20 low 100.20 touches it: out at 100.20. R 0.20, MAE 0, MFE 0.60
        Row("A21 a bar without an ATR after a level keeps the stop", Side.Buy, ["09:50 100.00/100.60/100.00/100.40", "10:20 100.40/100.40/100.20/100.25"], [B("10:00", 100.40, 0.40), B("10:15", 100.40, double.NaN)],
            new(ExitReason.Stop, "10:20", 100.20, 0.20, 0, 0.60), 1.0, 0),

        // A22: the mirror of A18 as a sell: entry 100.00 - 0.05 = 99.95; stop 99.40 in a minute of range 0.60: 99.40 + 0.18 = 99.58 up to 99.60. RFilled -(99.60 - 99.95) = 0.35
        Row("A22 slippage on the trailed stop of a sell", Side.Sell, ["09:59 100.00/100.00/99.60/99.60", "10:05 99.60/99.60/98.40/98.80", "10:20 98.80/99.40/98.80/99.30"], [B("10:00", 99.60, 0.5), B("10:15", 98.80, 0.5)],
            new(ExitReason.Stop, "10:20", 99.40, 0.60, 0, 1.60, RFilled: 0.35), 2.0, 1.0, modelled: true),
    ];

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_trade_is_the_one_computed_by_hand(TrailRow row) =>
        TrailHand.Check(row, new AtrTrailRule(0, (decimal)row.Multiple, (decimal)row.Activate));

    [Fact]
    public void A23_the_time_stop_leaves_at_the_open_after_its_bar_when_a_level_has_not_been_touched()
    {
        // A23: level 100.40 - 2 x 0.50 = 99.40 from 10:00 (never touched); the time stop of 2 bars, minimum progress 0.5, reads the close of bar 2 (10:15) at 100.40: progress 0.40 < 0.5, so the position leaves at the 10:15 open, 100.40. R 0.40, MAE 0, MFE 0.40
        var row = Row("A23 the time stop after a level", Side.Buy, ["09:50 100.00/100.40/100.00/100.40"], [B("10:00", 100.40, 0.5), B("10:15", 100.40, 0.5)],
            new(ExitReason.TimeStop, "10:15", 100.40, 0.40, 0, 0.40), 2.0, 0);

        TrailHand.Check(row, new AtrTrailRule(0, 2m, 0m), new LegPolicy(PartialKind.None, 0m, 0m, 0m, 0.5m, 2, 0));
    }

    [Fact]
    public void There_are_at_least_twenty_rows_of_both_sides_and_both_kinds_of_walk()
    {
        var rows = Rows.Select(r => r.Data).ToList();

        rows.Count.ShouldBeGreaterThanOrEqualTo(20);
        rows.Select(r => r.Side).Distinct().Count().ShouldBe(2);
        rows.Count(r => r.Hold > 0).ShouldBeGreaterThan(3);
        rows.Count(r => r.Activate == 0).ShouldBeGreaterThan(3);
        rows.Count(r => r.Activate > 0).ShouldBeGreaterThan(3);
        rows.Count(r => r.Modelled).ShouldBe(2);
    }
}
