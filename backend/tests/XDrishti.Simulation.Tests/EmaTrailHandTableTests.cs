using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 4 of P4-03: sixteen <c>EMA_TRAIL</c> rows computed by hand from D6 and D7: a bar that closes beyond its EMA (a buy: below) signals the whole position out at the open of the next minute that holds a trade, market-type, as
/// <see cref="ExitReason.EmaExit"/>. The signal on the first counted bar, a signal at the square-off bar (the square-off exits first), a stop and a signal at the same open (the stop), a gap at the signal open, a minute that falls to the
/// stop after the signal open (the open already left), a sell, two rows with slippage, a swing walk and a daily bar. The frame is that of <see cref="TrailRow"/>: entry 100.00, stop 99.00 (a sell 101.00), risk 1.00; the value of a tape bar is the EMA.
/// </summary>
public sealed class EmaTrailHandTableTests
{
    private static TrailRow Row(string name, Side side, string[] minutes, TrailBar[] tape, TrailWant want, string[]? tuesday = null, int hold = 0, bool modelled = false, string? endsAfter = null) =>
        new(name, side, minutes, tape, want, tuesday, hold, modelled, endsAfter);

    private static TrailBar B(string at, double close, double ema) => new(at, close, ema);

    public static TheoryData<TrailRow> Rows { get; } =
    [
        // E1 (D7): bar 3 closes 100.20 below its EMA 100.40; the next minute (10:30) opens 100.15: out at 100.15. R 0.15, MAE 0, MFE 0.60
        Row("E1 the worked example of D7", Side.Buy, ["09:50 100.00/100.50/100.00/100.50", "10:20 100.50/100.60/100.50/100.60", "10:29 100.60/100.60/100.20/100.20", "10:30 100.15/100.20/100.10/100.20"],
            [B("10:00", 100.50, 100.30), B("10:15", 100.50, 100.40), B("10:30", 100.20, 100.40)], new(ExitReason.EmaExit, "10:30", 100.15, 0.15, 0, 0.60)),

        // E2: the mirror SELL: bar 3 closes 99.80 above its EMA 99.60; 10:30 opens 99.85. R 0.15, MAE 0, MFE 0.60
        Row("E2 the mirror sell of E1", Side.Sell, ["09:50 100.00/100.00/99.50/99.50", "10:20 99.50/99.50/99.40/99.40", "10:29 99.40/99.80/99.40/99.80", "10:30 99.85/99.90/99.80/99.80"],
            [B("10:00", 99.50, 99.70), B("10:15", 99.50, 99.60), B("10:30", 99.80, 99.60)], new(ExitReason.EmaExit, "10:30", 99.85, 0.15, 0, 0.60)),

        // E3: the first counted bar closes 99.90 below the EMA 100.10 (the entry was made while the close was already beyond it): out at the open of 10:00, 99.95. R -0.05, MAE 0.10, MFE 0
        Row("E3 a signal on the first counted bar", Side.Buy, ["09:50 99.90/99.90/99.90/99.90", "10:00 99.95/99.95/99.90/99.90"], [B("10:00", 99.90, 100.10)], new(ExitReason.EmaExit, "10:00", 99.95, -0.05, 0.10, 0)),

        // E4: the signal is on the bar that closes at 15:15, the square-off minute: the square-off exits first, at its open 100.20. R 0.20, MAE 0, MFE 0.50
        Row("E4 a signal at the square-off bar makes no exit", Side.Buy, ["09:50 100.00/100.50/100.00/100.20"], [B("15:15", 100.20, 100.40)], new(ExitReason.SquareOff, "15:15", 100.20, 0.20, 0, 0.50)),

        // E5: the signal and the open at 98.80, through the stop 99.00, in the same minute: the stop takes it, at the open. R -1.20, MAE 1.20, MFE 0
        Row("E5 a stop and a signal at the same open: the stop", Side.Buy, ["09:50 100.00/100.00/99.50/99.50", "10:00 98.80/98.90/98.70/98.80"], [B("10:00", 99.50, 100.00)],
            new(ExitReason.Stop, "10:00", 98.80, -1.20, 1.20, 0, StopGap: true)),

        // E6: the next open gaps up to 101.00: the signal exit is at the open, whatever it is. R 1.00, MAE 0, MFE 1.00 (the open counts)
        Row("E6 a gap at the signal open", Side.Buy, ["09:50 100.00/100.50/100.00/100.20", "10:00 101.00/101.00/101.00/101.00"], [B("10:00", 100.20, 100.40)], new(ExitReason.EmaExit, "10:00", 101.00, 1.00, 0, 1.00)),

        // E7: the minute after the signal opens 100.10 and falls to 98.50 through the stop: the position has already left at the open (the stop is a resting order, the signal order filled first). R 0.10, MAE 0, MFE 0.50
        Row("E7 the minute that falls through the stop after the signal open", Side.Buy, ["09:50 100.00/100.50/100.00/100.20", "10:00 100.10/100.10/98.50/98.60"], [B("10:00", 100.20, 100.40)], new(ExitReason.EmaExit, "10:00", 100.10, 0.10, 0, 0.50)),

        // E8: a sell, the signal on the bar that closes at 15:15: the square-off open 99.80. R 0.20, MAE 0, MFE 0.50
        Row("E8 a sell signal at the square-off bar makes no exit", Side.Sell, ["09:50 100.00/100.00/99.50/99.80"], [B("15:15", 99.80, 99.60)], new(ExitReason.SquareOff, "15:15", 99.80, 0.20, 0, 0.50)),

        // E9: the EMA is not a number: no signal; the square-off open is 99.50. R -0.50, MAE 0.60 (the low 99.40), MFE 0
        Row("E9 an EMA that is not a number gives no signal", Side.Buy, ["09:50 100.00/100.00/99.40/99.50"], [B("10:00", 99.50, double.NaN), B("10:15", 99.40, double.NaN)], new(ExitReason.SquareOff, "15:15", 99.50, -0.50, 0.60, 0)),

        // E10: the close is equal to the EMA: not beyond it, no signal; the square-off open is 100.40. R 0.40, MAE 0, MFE 0.40
        Row("E10 a close equal to the EMA is not a signal", Side.Buy, ["09:50 100.00/100.40/100.00/100.40"], [B("10:00", 100.40, 100.40)], new(ExitReason.SquareOff, "15:15", 100.40, 0.40, 0, 0.40)),

        // E11: swing, hold 3: the signal is on Monday's last bar (closed 15:30): out at the first open of Tuesday, 100.35. R 0.35, MAE 0, MFE 0.50
        Row("E11 a signal on the last bar of a session leaves at the next open", Side.Buy, ["09:50 100.00/100.50/100.00/100.30"], [B("15:30", 100.30, 100.50)],
            new(ExitReason.EmaExit, "09:15", 100.35, 0.35, 0, 0.50, Sessions: 2, Day: 1), ["09:15 100.35/100.40/100.30/100.35"], 3),

        // E12: a daily bar closes 99.80 below its EMA 100.10: out at Tuesday's open 99.70. R -0.30, MAE 0.30, MFE 0
        Row("E12 a signal on a daily bar", Side.Buy, ["10:00 100.00/100.00/99.70/99.80"], [B("15:30", 99.80, 100.10)],
            new(ExitReason.EmaExit, "09:15", 99.70, -0.30, 0.30, 0, Sessions: 2, Day: 1), ["09:15 99.70/99.75/99.60/99.70"], 3),

        // E13: the data ends at 09:59, before the minute that would see the bar closed at 10:00: the end of the data at the close of 09:59, 100.20. R 0.20, MAE 0, MFE 0.50
        Row("E13 no minute after the signal bar is the end of the data", Side.Buy, ["09:50 100.00/100.50/100.00/100.20"], [B("10:00", 100.20, 100.40)], new(ExitReason.EndOfData, "09:59", 100.20, 0.20, 0, 0.50), null, 0, false, "09:59"),

        // E14: E2 with slippage: entry 100.00 - 0.05 = 99.95; the market exit at 99.85 in a minute of range 0.10 pays 0.05 + 0.005 = 0.055: 99.905 up to 99.95. RFilled -(99.95 - 99.95) = 0
        Row("E14 slippage on the signal exit of a sell", Side.Sell, ["09:50 100.00/100.00/99.50/99.50", "10:20 99.50/99.50/99.40/99.40", "10:29 99.40/99.80/99.40/99.80", "10:30 99.85/99.90/99.80/99.80"],
            [B("10:00", 99.50, 99.70), B("10:15", 99.50, 99.60), B("10:30", 99.80, 99.60)], new(ExitReason.EmaExit, "10:30", 99.85, 0.15, 0, 0.60, RFilled: 0.0), null, 0, true),

        // E15: E1 with slippage: entry 100.05; the exit at 100.15 in a minute of range 0.10 pays 0.055: 100.095 down to 100.05. RFilled 0
        Row("E15 slippage on the signal exit of a buy", Side.Buy, ["09:50 100.00/100.50/100.00/100.50", "10:20 100.50/100.60/100.50/100.60", "10:29 100.60/100.60/100.20/100.20", "10:30 100.15/100.20/100.10/100.20"],
            [B("10:00", 100.50, 100.30), B("10:15", 100.50, 100.40), B("10:30", 100.20, 100.40)], new(ExitReason.EmaExit, "10:30", 100.15, 0.15, 0, 0.60, RFilled: 0.0), null, 0, true),

        // E16: a sell whose next open gaps down to 99.00 (in its favour): out at the open. R 1.00, MAE 0, MFE 1.00
        Row("E16 a sell gap in favour at the signal open", Side.Sell, ["09:50 100.00/100.00/99.50/99.80", "10:00 99.00/99.00/99.00/99.00"], [B("10:00", 99.80, 99.60)], new(ExitReason.EmaExit, "10:00", 99.00, 1.00, 0, 1.00)),
    ];

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_trade_is_the_one_computed_by_hand(TrailRow row) => TrailHand.Check(row, new EmaTrailRule(0));

    [Fact]
    public void There_are_at_least_sixteen_rows_of_both_sides_and_both_kinds_of_walk()
    {
        var rows = Rows.Select(r => r.Data).ToList();

        rows.Count.ShouldBeGreaterThanOrEqualTo(16);
        rows.Select(r => r.Side).Distinct().Count().ShouldBe(2);
        rows.Count(r => r.Hold > 0).ShouldBeGreaterThan(1);
        rows.Count(r => r.Want.Reason == ExitReason.EmaExit).ShouldBeGreaterThan(8);
    }
}
