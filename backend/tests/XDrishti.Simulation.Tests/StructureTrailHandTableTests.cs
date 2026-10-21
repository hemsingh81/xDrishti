using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 3 of P4-03: sixteen <c>STRUCTURE_TRAIL</c> rows computed by hand from D4 and D5, buys and sells, intraday and swing: the level of a bar is the N-bar extreme the tape holds (the value of a row below), in force from the first minute at or
/// after the close of the bar if it is tighter than the stop in force; a looser level is ignored and counted; a value that is not a number gives no level; a level the price has passed leaves at the open. The frame is that of <see cref="TrailRow"/>:
/// entry 100.00, stop 99.00 (a sell 101.00), risk 1.00. <c>lookback_bars</c> 1 is "the previous candle"; a lookback longer than the history is a value that is not a number until the history is long enough.
/// </summary>
public sealed class StructureTrailHandTableTests
{
    private static TrailRow Row(string name, Side side, string[] minutes, TrailBar[] tape, TrailWant want, string[]? tuesday = null, int hold = 0, bool modelled = false, string? endsAfter = null) =>
        new(name, side, minutes, tape, want, tuesday, hold, modelled, endsAfter);

    private static TrailBar B(string at, double close, double level) => new(at, close, level);

    public static TheoryData<TrailRow> Rows { get; } =
    [
        // S1 (D5): lows 100.30 then 100.90 then 100.50: stops 100.30 (from 10:00), 100.90 (from 10:15); the third is looser: ignored and counted; 10:35 low 100.90: out at 100.90. R 0.90, MAE 0, MFE 1.00
        Row("S1 the worked example of D5", Side.Buy, ["09:50 100.00/100.60/100.00/100.60", "10:05 100.60/101.00/100.60/101.00", "10:35 101.00/101.00/100.90/100.95"],
            [B("10:00", 100.60, 100.30), B("10:15", 101.00, 100.90), B("10:30", 101.00, 100.50)], new(ExitReason.Stop, "10:35", 100.90, 0.90, 0, 1.00, Ignored: 1)),

        // S2: the mirror SELL with highs 99.70, 99.10, 99.50 (the last is looser for a sell): out at 99.10
        Row("S2 the mirror sell of S1", Side.Sell, ["09:50 100.00/100.00/99.40/99.40", "10:05 99.40/99.40/99.00/99.00", "10:35 99.00/99.10/99.00/99.05"],
            [B("10:00", 99.40, 99.70), B("10:15", 99.00, 99.10), B("10:30", 99.00, 99.50)], new(ExitReason.Stop, "10:35", 99.10, 0.90, 0, 1.00, Ignored: 1)),

        // S3: a level beyond the first counted bar: the N-bar low 98.50 reaches into earlier bars and is below the stop 99.00: ignored; the next, 99.40, is tighter; 10:20 low 99.40: out at 99.40. R -0.60, MAE 0.60, MFE 0
        Row("S3 a long lookback gives a level below the stop at first", Side.Buy, ["10:20 100.00/100.00/99.40/99.50"], [B("10:00", 100.00, 98.50), B("10:15", 100.00, 99.40)],
            new(ExitReason.Stop, "10:20", 99.40, -0.60, 0.60, 0, Ignored: 1)),

        // S4: the level 100.60 is above the price (100.30) when it takes effect at 10:00: a stop already passed fills at the open, not at the level. R 0.30, MAE 0, MFE 0.50
        Row("S4 a level at or above the price fills at the open", Side.Buy, ["09:50 100.00/100.50/100.00/100.30"], [B("10:00", 100.30, 100.60)],
            new(ExitReason.Stop, "10:00", 100.30, 0.30, 0, 0.50, StopGap: true)),

        // S5: the lookback is longer than the history: no value on either bar, the stop stays 99.00, the square-off open is 100.40. R 0.40, MAE 0, MFE 0.40
        Row("S5 a lookback longer than the history gives no level", Side.Buy, ["10:05 100.00/100.40/100.00/100.40"], [B("10:00", 100.40, double.NaN), B("10:15", 100.40, double.NaN)],
            new(ExitReason.SquareOff, "15:15", 100.40, 0.40, 0, 0.40)),

        // S6: the first counted bar already gives a level, 99.80 (the bar contains the fill minute): in force from 10:00; 10:30 low 99.70: out at 99.80. R -0.20, MAE 0.20, MFE 0
        Row("S6 the first counted bar acts", Side.Buy, ["10:30 100.00/100.00/99.70/99.90"], [B("10:00", 100.00, 99.80)], new(ExitReason.Stop, "10:30", 99.80, -0.20, 0.20, 0)),

        // S7: the minute 09:50 trades down to the initial stop before bar 1 has closed (its level 100.30 is not in force yet): out at 99.00. R -1.00, MAE 1.00, MFE 0
        Row("S7 a level is not in force inside the bar that made it", Side.Buy, ["09:50 100.00/100.00/98.90/99.00"], [B("10:00", 99.00, 100.30)], new(ExitReason.Stop, "09:50", 99.00, -1.00, 1.00, 0)),

        // S8: swing, hold 2: Monday's last bar gives 100.20, in force from Tuesday; Tuesday stays at 100.60: MAX_HOLD at the close of 15:29, 100.60. R 0.60, MAE 0, MFE 0.60
        Row("S8 MAX_HOLD after a level", Side.Buy, ["09:50 100.00/100.60/100.00/100.60"], [B("15:30", 100.60, 100.20)], new(ExitReason.MaxHold, "15:29", 100.60, 0.60, 0, 0.60, Sessions: 2, Day: 1), [], 2),

        // S9: a daily bar (lookback 2) gives 100.40; Tuesday opens 100.30, through it: out at the open. R 0.30, MAE 0, MFE 1.00
        Row("S9 a daily bar's level is passed by the next open", Side.Buy, ["10:00 100.00/101.00/100.00/100.80"], [B("15:30", 100.80, 100.40)],
            new(ExitReason.Stop, "09:15", 100.30, 0.30, 0, 1.00, StopGap: true, Sessions: 2, Day: 1), ["09:15 100.30/100.40/100.30/100.30"], 3),

        // S10: a sell: the high of the bar 99.40 is below the price 99.70: filled at the open 99.70. R 0.30, MAE 0, MFE 0.50
        Row("S10 a sell fills at the open when the level is behind the price", Side.Sell, ["09:50 100.00/100.00/99.50/99.70"], [B("10:00", 99.70, 99.40)],
            new(ExitReason.Stop, "10:00", 99.70, 0.30, 0, 0.50, StopGap: true)),

        // S11: the level 100.30 is never touched; the square-off open is 100.80. R 0.80, MAE 0, MFE 0.80
        Row("S11 the square-off after a level", Side.Buy, ["09:50 100.00/100.80/100.00/100.80"], [B("10:00", 100.80, 100.30)], new(ExitReason.SquareOff, "15:15", 100.80, 0.80, 0, 0.80)),

        // S12: the list ends at 14:00: the close of that minute, 100.80, is the end of the data. R 0.80, MAE 0, MFE 0.80
        Row("S12 the end of the data after a level", Side.Buy, ["09:50 100.00/100.80/100.00/100.80"], [B("10:00", 100.80, 100.30)], new(ExitReason.EndOfData, "14:00", 100.80, 0.80, 0, 0.80), null, 0, false, "14:00"),

        // S13: a level equal to the stop is not looser: accepted, nothing counted; 10:20 low 98.90 through 99.00: out at 99.00. R -1.00, MAE 1.00, MFE 0
        Row("S13 a level equal to the stop is not counted", Side.Buy, ["10:20 100.00/100.00/98.90/99.00"], [B("10:00", 100.00, 99.00)], new(ExitReason.Stop, "10:20", 99.00, -1.00, 1.00, 0)),

        // S14: levels 100.20, 100.40, 100.60 each tighter; 10:35 low 100.50 touches 100.60: out at 100.60. R 0.60, MAE 0, MFE 0.90
        Row("S14 a rising run of levels", Side.Buy, ["09:50 100.00/100.50/100.20/100.50", "10:05 100.50/100.70/100.40/100.70", "10:20 100.70/100.90/100.60/100.90", "10:35 100.90/100.90/100.50/100.60"],
            [B("10:00", 100.50, 100.20), B("10:15", 100.70, 100.40), B("10:30", 100.90, 100.60)], new(ExitReason.Stop, "10:35", 100.60, 0.60, 0, 0.90)),

        // S15: slippage: entry 100.05; the stop 100.30 in a minute of range 0.30 pays 0.15 + 0.05 x 0.30 = 0.165: 100.135 down to 100.10. RFilled (100.10 - 100.05) = 0.05
        Row("S15 slippage on the structure stop", Side.Buy, ["09:50 100.00/100.60/100.00/100.60", "10:20 100.60/100.60/100.30/100.40"], [B("10:00", 100.60, 100.30)],
            new(ExitReason.Stop, "10:20", 100.30, 0.30, 0, 0.60, RFilled: 0.05), null, 0, true),

        // S16: bar 1 has no value, bar 2 gives 100.80 from 10:15; 10:20 low 100.70: out at 100.80. R 0.80, MAE 0, MFE 1.00
        Row("S16 a value that is not ready and then one that is", Side.Buy, ["10:05 100.00/101.00/100.00/101.00", "10:20 101.00/101.00/100.70/100.75"], [B("10:00", 101.00, double.NaN), B("10:15", 101.00, 100.80)],
            new(ExitReason.Stop, "10:20", 100.80, 0.80, 0, 1.00)),
    ];

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_trade_is_the_one_computed_by_hand(TrailRow row) => TrailHand.Check(row, new StructureTrailRule(0));

    [Fact]
    public void There_are_at_least_sixteen_rows_of_both_sides_and_both_kinds_of_walk()
    {
        var rows = Rows.Select(r => r.Data).ToList();

        rows.Count.ShouldBeGreaterThanOrEqualTo(16);
        rows.Select(r => r.Side).Distinct().Count().ShouldBe(2);
        rows.Count(r => r.Hold > 0).ShouldBeGreaterThanOrEqualTo(2);
    }
}
